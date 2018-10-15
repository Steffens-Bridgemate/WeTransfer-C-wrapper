# WeTransfer-C#-wrapper
Provides the source code for a C# wrapper around the WeTransfer public API<br/>

HOW TO USE THE V1 API<br/>
Use as follows:<br/>
The Program.cs file in the Console application gives an example how to use the Communicator class.<br/>
 // Fill in your API-key and the directory where the chunks for partial upload can be created.<br/>
  var uploader = new Communicator(apiKey: "your_secret_API_key",<br/>
                                  chunkDirectory:@"The directory you want to use to store chuncks of split files.");<br/>
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

HOW TO USE THE V2 API <br/>
Unit tests document pretty much how the TransferApiCommunicator and BoardApiCommunicator classes should be used.

TRANSFERAPI
  public void UploadTwoFiles()
        {
            //Act
            var files = new List<FileInfo>();
            foreach (string fileName in new[] { "TextFile1.txt", "picture.jpg" })
            {
                var info = new FileInfo(Path.Combine(_appPath, "Chunks", fileName));
                files.Add(info);
            }
            var result=  _communicator.UploadFiles(files.Select(file => file.FullName), "Testtransfer",_user).Result;
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.DownloadUrl));
            Debug.Print(result.DownloadUrl);
        }

BOARDAPI
// boardResponse.Id is the Board Id. This id is needed for the calls to add files, add links and get Board info.
 public void AddTwoFilesToBoard()
        {
            //Arrange
            BoardApiCommunicator.ClearToken();
            _communicator.GetToken("Username").Wait();
            var boardResponse = _communicator.CreateBoard(_user, "My beautiful board").Result;

            var fileRequests = new List<(string name,int size,string fullPath)>();
            var infos = new List<string>();
            foreach (string fileName in new[] { "TextFile1.txt","picture.jpg" })
            {
                var info = new FileInfo(Path.Combine(_appPath, "Chunks", fileName));
                infos.Add(info.FullName);
                fileRequests.Add((name: info.Name, size: (int)info.Length, fullPath: info.FullName));
            }

            //Act
            var fileUploadResponse = _communicator.UploadFilesToBoard(boardResponse.Id, infos,_user).Result;

            //Assert
            Assert.AreEqual(UploadResultV2.ResultCode.Success, fileUploadResponse.Result);

            //Act: get board information
            var infoResponse = _communicator.GetBoardInfo(boardResponse.Id).Result;
        }
        
        public void AddLinksToBoard()
        {
            //Arrange
            var boardResponse = _communicator.CreateBoard(_user,"Links board").Result;
            var links = new[] { (url: "http://wetransfer.com", title: "End user portal for We Transfer"),
                                (url: "http://developers.wetransfer.com",title:"Developers portal for We Transfer")};

            //Act
            var response= _communicator.AddLinks(boardResponse.Id, links).Result;

            //Assert
            Assert.IsTrue(response.statusCode == HttpStatusCode.Created);
            Assert.AreEqual(2,response.responseArray.Length);
        }
            
