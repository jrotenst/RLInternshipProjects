using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using RestSharp;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace OCRTesting
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                DirectoryInfo root = new DirectoryInfo(@"C:\ProjectDocs");
                DirectoryInfo[] invoiceDirectories = root.GetDirectories();
                string textFile = @"C:\Users\Davelsys\source\repos\OCRTesting\OCRTesting\VeryfiResponses.txt";

                GetOCRData(invoiceDirectories, textFile);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }           
        }

        // takes directory and text file, gets all subdirectories, gets all files, processes them, and writes results to text file
        public static void GetOCRData(DirectoryInfo[] directory, string textFile)
        {           
            foreach (DirectoryInfo dir in directory)
            {
                string[] files = Directory.GetFiles(dir.FullName);

                foreach (string file in files)
                {
                    Console.WriteLine($"Loading file: {file}\n");

                    if (!file.EndsWith("pdf") && !file.EndsWith("jpeg") 
                        && !file.EndsWith("jpg") && !file.EndsWith("png"))
                    {
                        Console.WriteLine("Skipping document, unsupported file format.");
                    }
                    else
                    {
                        try
                        {
                            JObject ocr = CallVeryfiAPI(file);
                            Console.WriteLine("Response received, writing to file...\n");

                            string date = (string)ocr["date"];
                            double total = (double)ocr["total"];
                            string vendorName = (string)ocr["vendor"]["name"];
                            string line = $"{file},{date},{total},{vendorName},\n";
                            File.AppendAllText(textFile, line);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message + "\n");
                        }
                    }
                }
            }
        }

        public static JObject CallVeryfiAPI(string filePath)
        {
            var clientID = "vrf8ECjAt3ANdZsx9Y2O2BFjmEXHf4dYJDnkW8F";
            string apiKey = "apikey yrotenstreich:6c0242aada65ddad4be46c644cd02a4a";
            var url = "https://api.veryfi.com/api/v7/partner/documents/";
            //string filePath = @"C:\PayablesDocs\PayablesDocs\Invoices\37491\doc02317720160131190116.pdf";

            var client = new RestClient("https://api.veryfi.com/api/v7/partner/" +
                "documents/?=6c0242aada65ddad4be46c644cd02a4a");
            client.Timeout = -1;
            var request = new RestRequest(Method.POST);

            // add headers
            request.AddHeader("CLIENT-ID", clientID);
            request.AddHeader("AUTHORIZATION", apiKey);
            request.AddHeader("Accept", "application/json");

            // add body
            request.AddFile("file", filePath);
            request.AddParameter("file_name", filePath);

            // get response and convert to JObject
            IRestResponse response = client.Execute(request);
            return JObject.Parse(response.Content.ToString());
        }

    }
}
