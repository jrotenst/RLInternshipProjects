using Amazon;
using Amazon.S3;
using Amazon.S3.IO;
using Amazon.S3.Model;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;



namespace AWSS3DocScanner
{
    class Program
    {
        private static readonly string DEF_BUCKET = "rldocs";
        private static readonly string DEF_KEY = "";

        private static string accessKey;
        private static string secretKey;
        private static bool authorized = false;
        private static string bucket;
        private static string key;
        private static ScannedDirectoryData sdd;
        private static DateTime scanTime = DateTime.Now;
        private static Stopwatch stopwatch = new Stopwatch();
        private static S3FileDBDataContext db = new S3FileDBDataContext();
        private static List<AWSS3Object> insertionList = new List<AWSS3Object>();
        private static List<ScannedDirectoryData> processedDirectories = new List<ScannedDirectoryData>();
        
        private static int dirNum = 0;
        private static int scanned = 0;
        private static int updated = 0;
        private static int inserted = 0;
        private static int totalScanned = 0;
        private static int totalUpdated = 0;
        private static int totalInserted = 0;

        private static string logName = $"log {DateTime.Now.ToString("yyyy-MM-dd_hh")}.txt";

        static void Main(string[] args)
        {
            stopwatch.Start();

            WriteLine("\r\n\r\n***** " + DateTime.Now + " *****\r\n");

            getPrefs(args);

            while (!authorized)
            {
                GetCredentials();

                // scan S3
                using (var client = new AmazonS3Client(accessKey, secretKey, RegionEndpoint.USEast1))
                {
                    try
                    {
                        S3DirectoryInfo root = new S3DirectoryInfo(client, bucket, key);
                        S3DirectoryInfo[] dirs = root.EnumerateDirectories().ToArray();

                        authorized = true;
                        bool run = true;

                        while (run)
                        {
                            DisplayDirectories(dirs);

                            WriteLine("\r\nEnter directory number to process directory, or enter 0 to process all.");
                            string response = Console.ReadLine();
                            bool success = int.TryParse(response, out dirNum);

                            while (!success || dirNum < 0 || dirNum > dirs.Length)
                            {
                                WriteLine("\r\nInvalid entry. Enter 0 or a directory number:");
                                response = Console.ReadLine();
                                success = int.TryParse(response, out dirNum);
                            }

                            if (dirNum == 0)
                            {
                                sdd = new ScannedDirectoryData(root.FullName);

                                // process files in root directory
                                WriteLine($"Scanning files in {root.FullName}...");

                                foreach (S3FileInfo f in root.EnumerateFiles())
                                {
                                    try
                                    {
                                        ScanFile(f);
                                    }
                                    catch (Exception e)
                                    {
                                        LogError(e.Message);
                                    }
                                }
                                db.AWSS3Objects.InsertAllOnSubmit(insertionList);
                                SubmitBatch();

                                // process each sub directory recursively and submit
                                foreach (S3DirectoryInfo d in dirs)
                                {
                                    ProcessDirectory(d);
                                }
                                run = false;
                            }
                            else
                            {
                                ProcessDirectory(dirs[dirNum - 1]);
                                WriteLine($"\r\nDo you want to scan another directory? (Y/N)");
                                response = Console.ReadLine();

                                while (response.ToUpper() != "Y" && response.ToUpper() != "N")
                                {
                                    WriteLine("\r\nInvalid response. Enter Y or N:");
                                    response = Console.ReadLine();
                                }
                                run = response.ToUpper() == "Y";
                            }
                        }

                        DisplayTotals();
                        stopwatch.Stop();

                        WriteLine($"\r\nTime elapsed: {stopwatch.Elapsed}");
                    }
                    catch(Exception e)
                    {
                        LogError("UNAUTHORIZED");
                    }
                }


            }
            Console.ReadKey();
        }

        private static void getPrefs(string[] args)
        {
            bucket = DEF_BUCKET;
            key = DEF_KEY;

            int i = 0;

            if (args.Length > i)
            {
                bucket = args[i++];
            }
            
            if (args.Length > i)
            {
                key = args[i++];
            }
        }

        private static void GetCredentials()
        {
            WriteLine("\r\nEnter AWS access key:");
            accessKey = Console.ReadLine();
            WriteLine("\r\nEnter AWS secret access key:");
            secretKey = Console.ReadLine();
        }

        private static void DisplayDirectories(S3DirectoryInfo[] dirs)
        {
            for (int i = 0; i < dirs.Length; i+= 2)
            {
                if (i < 10)
                {
                    Console.Write(" ");
                }
                if (i < 100)
                {
                    Console.Write(" ");
                }
                if (i + 2 >= dirs.Length)
                {
                    WriteLine($"{i + 1}) {dirs[i].Name}");
                }
                else
                {
                    string spaces = new String(' ', 30 - dirs[i].Name.Length);
                    WriteLine($"{i + 1}) {dirs[i].Name}{spaces}{i + 2}) {dirs[i + 1].Name}"); 
                }
            }
            
        }

        private static void ProcessDirectory(S3DirectoryInfo dir)
        {
            sdd = new ScannedDirectoryData(dir.FullName);

            WriteLine($"\r\nProcessing directory: {dir.FullName}");

            ScanDirectory(dir);
            SubmitBatch();   
            DisplayResults();
            processedDirectories.Add(sdd);
        }

        private static void ScanDirectory(S3DirectoryInfo dir)
        {
            var files = dir.EnumerateFiles();
            var subDirs = dir.EnumerateDirectories();

            if (subDirs.Any())
            {
                WriteLine($"\r\nProcessing {subDirs.Count()} directories in {dir.Name}...");

                foreach (S3DirectoryInfo sub in subDirs)
                {
                    ScanDirectory(sub);
                }
            }
            if (files.Any())
            {
                WriteLine($"\r\nScanning Files in {dir.Name}...");

                foreach (S3FileInfo file in files)
                {
                    try
                    {
                        ScanFile(file);
                    }
                    catch(Exception e)
                    {
                        LogError(e.Message);
                    }
                }               
            }           
        }

        private static void ScanFile(S3FileInfo file)
        {            
            if (scanned >= 5000)
            {
                SubmitBatch();
            }
            
            DateTime fileModified = new SqlDateTime(GetLastModifiedFromS3(file.FullName)).Value;

            // if file is already in database
            try
            {
                AWSS3Object dbFile = db.AWSS3Objects.Single(f => f.FileKey == file.FullName);

                // if file was modified, update database record
                if (!fileModified.Equals(dbFile.FileModified))
                {
                    UpdateFile(file, dbFile);
                }
                else
                {
                    dbFile.LastScanned = scanTime;
                }
            }
            catch(Exception e)
            {
                InsertFile(file);
            }
            scanned++;  
        }

        private static void InsertFile(S3FileInfo file)
        {            
            AWSS3Object dbFile = new AWSS3Object()
            {
                FileKey = file.FullName,
                FileName = file.Name,
                FileModified = GetLastModifiedFromS3(file.FullName),
                FileSize = file.Length,
                WindowsFilePath = GetLocalNameFromS3(file.FullName),
                LastScanned = scanTime
            };
            insertionList.Add(dbFile);
            inserted++;
        }   

        private static void UpdateFile(S3FileInfo file, AWSS3Object dbFile)
        {
            dbFile.FileModified = GetLastModifiedFromS3(file.FullName);
            dbFile.FileSize = file.Length;
            dbFile.LastScanned = scanTime;
            updated++;
        }

        private static DateTime GetLastModifiedFromS3(string fl)
        {
            DateTime lastModified = DateTime.Now;

            var pattern = @"(@!@(?:.*?)@!@)";

            string[] stringParts = System.Text.RegularExpressions.Regex.Split(fl, pattern);
            for (int i = 0; i < stringParts.Length; i++)
            {
                var part = stringParts[i];

                if (part.StartsWith("@!@") && part.EndsWith("@!@"))
                {
                    string encoded = part.Replace("@!@", "");

                    if (i == stringParts.Length - 2)
                    {
                        DateTime.TryParseExact(encoded, "yyyy-MM-dd HH.mm.ss.fff", System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out lastModified);
                    }
                }
            }
            return lastModified;
        }
        private static string GetLocalNameFromS3(string fl)
        {
            DateTime lastModifiedDate = DateTime.Now;

            var localFileBuilder = new System.Text.StringBuilder();

            var pattern = @"(@!@(?:.*?)@!@)";

            string[] stringParts = System.Text.RegularExpressions.Regex.Split(fl, pattern);
            for (int i = 0; i < stringParts.Length; i++)
            {
                var part = stringParts[i];

                if (part.StartsWith("@!@") && part.EndsWith("@!@"))
                {
                    string encoded = part.Replace("@!@", "");

                    //check if it's a hex number
                    if (encoded.Length == 2 && IsHex(encoded.ToCharArray()))
                    {
                        localFileBuilder.Append(Convert.ToChar(Convert.ToUInt32(encoded.Substring(0, 2), 16)));
                    }
                    else if (i == (stringParts.Length - 2) && DateTime.TryParseExact(encoded, "yyyy-MM-dd HH.mm.ss.fff", System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out lastModifiedDate))
                    {
                        //this is the last modified date - if saving the file, do File.SetLastWriteTime() with this date
                    }
                    else
                    {
                        localFileBuilder.Append(part);
                    }
                }
                else
                {
                    localFileBuilder.Append(part);
                }
            }

            return localFileBuilder.ToString();
        }

        private static bool IsHex(IEnumerable<char> chars)
        {
            bool isHex;
            foreach (var c in chars)
            {
                isHex = ((c >= '0' && c <= '9') ||
                         (c >= 'a' && c <= 'f') ||
                         (c >= 'A' && c <= 'F'));

                if (!isHex)
                {
                    return false;
                }
            }
            return true;
        }

        private static void SubmitBatch()
        {
            sdd.Batch++;
            WriteLine($"\r\nSubmitting batch {sdd.Batch} of {sdd.RootDirectory}...");
            db.AWSS3Objects.InsertAllOnSubmit(insertionList);
            try
            {
                db.SubmitChanges();
                WriteLine($"\r\nSuccessfully submitted changes to database!\n\n" +
                    $"Files Scanned: {scanned}\n" +
                    $"Files Updated: {updated}\n" +
                    $"Files Inserted: {inserted}");
            }
            catch(Exception e)
            {
                LogError(e.Message);
            }
            
            // close connection and create new context
            db.Connection.Close();
            db.Dispose();
            db = new S3FileDBDataContext();

            insertionList.Clear();
            sdd.Scanned += scanned;
            sdd.Updated += updated;
            sdd.Inserted += inserted;
            scanned = 0;
            updated = 0;
            inserted = 0;
        }

        private static void DisplayResults()
        {
            WriteLine($"\r\n***** DIRECTORY SCAN COMPLETE *****\r\n" +
                $"Directory: {sdd.RootDirectory}\n" +
                $"Files Scanned: {sdd.Scanned}\n" +
                $"Files Updated: {sdd.Updated}\n" +
                $"Files Inserted: {sdd.Inserted}");
        }

        private static void DisplayTotals()
        {
            foreach(ScannedDirectoryData dd in processedDirectories)
            {
                totalScanned += dd.Scanned;
                totalUpdated += dd.Updated;
                totalInserted += dd.Inserted;                
            }
            
            WriteLine($"\r\n***** TOTALS: *****\n\n" +
                $"Files Scanned: {totalScanned}\n" +
                $"Files Updated: {totalUpdated}\n" +
                $"Files Inserted: {totalInserted}");
        }

        private static void WriteLine(string line)
        {
            Console.WriteLine(line);

            using (TextWriter writer = new StreamWriter(logName, true))
            {
                writer.WriteLine(line);
            }
        }
        private static void LogError(string error)
        {
            WriteLine("\r\n ***** ERROR: " + error + " *****");
        }

    }
}
