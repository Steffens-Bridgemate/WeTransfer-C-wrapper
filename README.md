# WeTransfer-C-wrapper
Provides the source code for a C# wrapper around the WeTransfer public API<br/>
Use as follows:<br/>
The Program.cs file in the Console application gives an example how to use the Communicator class.<br/>
 // Fill in your API-key and the directory where the chunks for partial upload can be created.<br/>
  var uploader = new Communicator(apiKey: "your_secret_API_key",<br/>
                                  chunkDirectory:@"The directory you want to use to store chucks of split files.");<br/>
  <br/>
  var progress = new Progress<ProgressReport>();<br/>
  progress.ProgressChanged += (sender,report) => Console.WriteLine($"{report.Message}: {report.Percentage}");<br/>
  <br/>
// Fill in the full paths to the file(s) you want to upload<br/>
  var result = uploader.UploadFiles(new[] { @"Full path to the first file",<br/>
                                            @"Full path to the second file" },<br/>
                                    "My beautiful upload request",<br/>
                                     progress).Result;<br/>
  <br/>
// The result.DownloadUrl can be used by the recipient to download the uploaded files.
