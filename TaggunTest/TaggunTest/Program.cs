using System;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using System.Text;

namespace TaggunTest
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                DirectoryInfo root = new DirectoryInfo(@"C:\PayablesDocs\PayablesDocs\Invoices");
                DirectoryInfo[] invoiceDirectories = root.GetDirectories();

                List<string> lines = new List<string>();

                foreach (DirectoryInfo dir in invoiceDirectories)
                {
                    string[] files = Directory.GetFiles(dir.FullName);

                    foreach (string file in files)
                    {
                        Console.WriteLine(file + "\n");

                        try
                        {
                            JObject ocr = getTaggunData(file);

                            Console.WriteLine("Response received, writing to csv file...\n");

                            double conLevel = (double)ocr["confidenceLevel"];
                            double total = (double)ocr["totalAmount"]["data"];
                            string date = (string)ocr["date"]["data"];
                            string vendorName = (string)ocr["merchantName"]["data"];
                            string jsonString = ocr.ToString(Formatting.None);

                            /*StringBuilder sb = new StringBuilder();
                            List<char> jsonList = jsonString.ToList();
                            jsonList.ForEach(x => sb.Append(x.Equals(',') ? "\",\"" : x.ToString()));
                            jsonString = sb.ToString();*/


                         
                            

                            lines.Add($"\"{file}\"!{conLevel}!{total}!\"{date}\"!\"{vendorName}\"!\"{jsonString}\"!");
                            
                            string filePath = @"C:\Users\Davelsys\source\repos\TaggunTest\TaggunTest\TaggunCSV.txt";

                            File.WriteAllLines(filePath, lines);

                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message + "\n");
                        }
                    }
                }


            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

        }

        public class InvoiceOCR
        {
            public string fileName { get; set; }
            public double totalAmount { get;  set; }
            public decimal confidenceLevel { get; set; }
        }

        public static JObject getTaggunData(string fileName)
        {
            var taggunApiKey = "7a35ddf0c20f11eaafc7c5a18819396c";
            var taggunApiUrl = "https://api.taggun.io/api/receipt/v1/simple/file";

            byte[] fileData = System.IO.File.ReadAllBytes(fileName);

            var timeStart = DateTime.Now;

            using (var httpClient = new HttpClient { Timeout = new TimeSpan(0, 0, 0, 60, 0) })
            {
                string fileType = GetFileType(fileName);
                
                if (fileType == "UNSUPPORTED")
                {
                    throw new Exception("Error: file type not supported");
                }

                HttpResponseMessage response = null;

                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("apikey", taggunApiKey);

                var parentContent = new MultipartFormDataContent("----WebKitFormBoundaryfzdR3Imh7urK8qw");

                var documentContent = new ByteArrayContent(fileData);
                documentContent.Headers.Remove("Content-Type");
                documentContent.Headers.Remove("Content-Disposition");
                documentContent.Headers.TryAddWithoutValidation("Content-Type", fileType);
                documentContent.Headers.TryAddWithoutValidation("Content-Disposition",
                string.Format(@"form-data; name=""file""; filename=""{0}""", fileName));
                parentContent.Add(documentContent);

                var refreshContent = new StringContent("false");
                refreshContent.Headers.Remove("Content-Type");
                refreshContent.Headers.Remove("Content-Disposition");
                refreshContent.Headers.TryAddWithoutValidation("Content-Disposition", @"form-data; name=""refresh""");
                parentContent.Add(refreshContent);

                response = httpClient.PostAsync(taggunApiUrl, parentContent).Result;
                response.EnsureSuccessStatusCode();
                var result = response.Content.ReadAsStringAsync().Result;
                return JObject.Parse(result);
                
            }
        }

        public static string GetFileType(string file)
        {
            if (file.EndsWith("pdf"))
            {
                return "application/pdf";
            }
            if (file.EndsWith("jpg"))
            {
                return "image/jpeg";
            }
            if (file.EndsWith("png"))
            {
                return "image/png";
            }
            if (file.EndsWith("gif"))
            {
                return "image/gif";
            }
            return "UNSUPPORTED";
        }
    }

}
