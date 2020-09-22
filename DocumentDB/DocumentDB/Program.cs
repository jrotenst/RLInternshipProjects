using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocumentDB
{
    /// <summary>
    /// This program scans all files in a given folder, updates their records in the database, and does the same to its subfolders.
    /// 1. Takes a root path and retrieves all of its files, including files in subdirectories.
    /// 2. If the file is already in the database, the file properties are updated. If the file is not in the database, the file is inserted. 
    /// 3. All scheduled changes are submitted.
    /// </summary>
    static class Program
    {
        private static ScannedDirectoryData sdd;
        private static DateTime scanTime = DateTime.Now;
        private static Stopwatch stopwatch = new Stopwatch();
        private static DocsDBDataContext db = new DocsDBDataContext();
        private static List<DocList> insertionList = new List<DocList>();
        private static List<ScannedDirectoryData> processedDirectories = new List<ScannedDirectoryData>();

        private static int dirNum = 0;
        private static int scanned = 0;
        private static int updated = 0;
        private static int inserted = 0;
        private static int totalScanned = 0;
        private static int totalUpdated = 0;
        private static int totalInserted = 0;

        private static string logName = $"log {DateTime.Now.ToString("yyyy-MM-dd_hh")}.txt";

        public static void Main(string[] args)
        {
            stopwatch.Start();

            WriteLine($"\r\n\r\n***** {DateTime.Now} *****\r\n");

            Console.WriteLine("\r\nEnter root directory path:\n");
            string rootPath = Console.ReadLine();

            while (!Directory.Exists(rootPath))
            {
                Console.WriteLine("\r\nFile does not exist. \n\nPlease Enter a new file name:");
                rootPath = Console.ReadLine();
            }

            try
            {
                DirectoryInfo root = new DirectoryInfo(rootPath);
                DirectoryInfo[] dirs = root.GetDirectories();
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
                        WriteLine($"\r\nScanning files in {root.FullName}...");

                        FileInfo[] rootFiles = root.GetFiles();

                        if (rootFiles.Length > 0)
                        {
                            foreach (FileInfo f in rootFiles)
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
                            SubmitBatch();
                        }

                        foreach (DirectoryInfo dir in dirs)
                        {
                            ProcessDirectory(dir);
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
            }
            catch(Exception e)
            {
                LogError(e.Message);
            }

            stopwatch.Stop();

            WriteLine($"\r\nTime elapsed: {stopwatch.Elapsed}");

            Console.ReadKey();
        }

        private static void DisplayDirectories(DirectoryInfo[] dirs)
        {
            WriteLine("\r\n");

            for (int i = 0; i < dirs.Length; i += 2)
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

        private static void ProcessDirectory(DirectoryInfo dir)
        {
            sdd = new ScannedDirectoryData(dir.FullName);

            WriteLine($"\r\nProcessing directory: {dir.FullName}");

            ScanDirectory(dir);
            SubmitBatch();
            DisplayResults();
            processedDirectories.Add(sdd);
        }

        private static void ScanDirectory(DirectoryInfo dir)
        {
            var files = dir.EnumerateFiles();
            var subDirs = dir.EnumerateDirectories();

            if (subDirs.Any())
            {
                WriteLine($"\r\nProcessing {subDirs.Count()} directories in {dir.Name}...");

                foreach (DirectoryInfo sub in subDirs)
                {
                    ScanDirectory(sub);
                }
            }
            if (files.Any())
            {
                WriteLine($"\r\nScanning Files in {dir.Name}...");

                foreach (FileInfo file in files)
                {
                    try
                    {
                        ScanFile(file);
                    }
                    catch (Exception e)
                    {
                        LogError(e.Message);
                    }
                }
            }
        }

        public static void ScanFile(FileInfo file)
        {
            if (scanned >= 5000)
            {
                SubmitBatch();
            }

            DateTime fileCreated = new SqlDateTime(file.CreationTime).Value;
            DateTime fileModified = new SqlDateTime(file.LastWriteTime).Value;

            // if file is already in database
            try 
            {
                DocList dl = db.DocLists.Single(d => d.DocPath == file.FullName && d.DocCreated.Equals(fileCreated));

                // if file was modified, update database record
                if (!fileModified.Equals(dl.DocModified))
                {
                    UpdateFile(file, dl);
                }
                else
                {
                    dl.LastScanned = scanTime;
                }
                
            }
            catch(Exception e)
            {
                InsertFile(file);
            }
            scanned++;
        }

        public static void InsertFile(FileInfo file)
        {
            DocList document = new DocList()
            {
                DocPath = file.FullName,
                DocName = file.Name,
                DocCreated = file.CreationTime,
                DocModified = file.LastWriteTime,
                DocSize = file.Length,
                LastScanned = scanTime
            };

            insertionList.Add(document);
            inserted++;
        }

        public static void UpdateFile(FileInfo file, DocList dl)
        {
            dl.DocModified = file.LastWriteTime;
            dl.DocSize = file.Length;
            dl.LastScanned = scanTime;
            updated++;
        }

        private static void SubmitBatch()
        {
            sdd.Batch++;

            WriteLine($"\r\nSubmitting batch {sdd.Batch} of {sdd.RootDirectory}...");

            db.DocLists.InsertAllOnSubmit(insertionList);

            try
            {
                db.SubmitChanges();
                WriteLine($"\r\nSuccessfully submitted changes to database!\n\n" +
                    $"Files Scanned: {scanned}\n" +
                    $"Files Updated: {updated}\n" +
                    $"Files Inserted: {inserted}");
            }
            catch (Exception e)
            {
                LogError(e.Message);
            }

            // close connection and create new context
            db.Connection.Close();
            db.Dispose();

            db = new DocsDBDataContext();

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
            foreach (ScannedDirectoryData dd in processedDirectories)
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

    
