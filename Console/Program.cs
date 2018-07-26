using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WeTransferUploader;

namespace TestingConsole
{
    class Program
    {
        static void Main(string[] args)
        {

            // Fill in your API-key and the directory where the chunks for partial upload can be created.
            var uploader = new Communicator(apiKey: "your_secret_API_key",
                                            chunkDirectory:@"The directory you want to use to store chucks of split files.");

            var progress = new Progress<ProgressReport>();
            progress.ProgressChanged += (sender,report) => Console.WriteLine($"{report.Message}: {report.Percentage}");

            var result = uploader.UploadFiles(new[] { @"Full path to the first file",
                                                      @"Full path to the second file" },
                                             "My beautiful upload request",
                                             progress).Result;

            Console.WriteLine($"Result at stage: {result.CurrentStage}: {result.Result} ({result.Message}).");

            if (result.Result == UploadResult.ResultCode.Success)
                Console.WriteLine($"File can be downloaded from: {result.DownloadUrl}");
            else
                Console.WriteLine($"Upload failed with code: {result.Result} ({result.Message}).");

            Console.ReadLine();
        }
    }
}
