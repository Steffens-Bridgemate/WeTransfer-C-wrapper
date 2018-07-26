# WeTransfer-C-wrapper
Provides the source code for a C# wrapper around the WeTransfer public API
Use as follows:
The Program.cs file in the Console application gives an example how to use the Communicator class.
 // Fill in your API-key and the directory where the chunks for partial upload can be created.
  var uploader = new Communicator(apiKey: "your_secret_API_key",
                                  chunkDirectory:@"The directory you want to use to store chucks of split files.");
  
  var progress = new Progress<ProgressReport>();
  progress.ProgressChanged += (sender,report) => Console.WriteLine($"{report.Message}: {report.Percentage}");
  
// Fill in the full paths to the file(s) you want to upload
  var result = uploader.UploadFiles(new[] { @"Full path to the first file",
                                            @"Full path to the second file" },
                                    "My beautiful upload request",
                                     progress).Result;
  
// The result.DownloadUrl can be used by the recipient to download the uploaded files.
