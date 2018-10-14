using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using WeTransferUploader.V2;

namespace V2Tests
{
    [TestClass]
    public class V2Tests
    {

        private const string ApiKey= "Fill in your own API-key here";
        private CommunicatorV2 _communicator;
        private string _appPath;
        private string _user;
        private string _token;

        [TestInitialize]
        public void Initialize()
        {
            if (string.IsNullOrEmpty(ApiKey))
                throw new ArgumentNullException("Fill in the value of your personal api key. The api key can be obtained from WeTransfer.");
            _appPath = AppDomain.CurrentDomain.BaseDirectory;
            _user = "Tester";
            var chunkDirectory = Path.Combine(_appPath, "Chunks");
            _communicator = new CommunicatorV2(ApiKey,chunkDirectory);
            if (CommunicatorV2.Token == null)
                _communicator.GetToken(_user).Wait();
        }

        private FileUploadRequestContent CreateUploadRequest()
        {
            var fileRequests = new List<FileRequestContent>();
            foreach(string fileName in new[] {"TextFile1.txt","picture.jpg" })
            {
                var info = new FileInfo(Path.Combine(_appPath, "Chunks", fileName));
                var request = new FileRequestContent(info.Name, (int)info.Length,info.FullName);
                fileRequests.Add(request);
            }
            var files = new FileUploadRequestContent("test", fileRequests.ToArray());
            return files;
           
        }

        [TestMethod]
        public void GetToken()
        {
            //Arrange
            CommunicatorV2.ClearToken();
            var token = CommunicatorV2.Token;
            Assert.IsNull(CommunicatorV2.Token);

            //Act
            var response = _communicator.GetToken(_user).Result;

            //Assert
            Assert.IsTrue(response.Success.Value);
            token = response.Token;
            Assert.IsFalse(string.IsNullOrEmpty(token));
            _token = token;
        }

        [TestMethod]
        public void CreateTransfer()
        {
            //Arrange
            var files = CreateUploadRequest();

            //Act
            var response = _communicator.CreateTransfer(files).Result;

            //Assert
            Assert.IsTrue(response.Success.Value);
            Assert.AreEqual(2,response.Files.Length);
            Assert.IsTrue(response.Files.All(f => f.ChunkData.ChunkSize > 0));
        }

        [TestMethod]
        public void RequestUploadUrls()
        {
            //Arrange
            var files = CreateUploadRequest();
            var response = _communicator.CreateTransfer(files).Result;

            //Act
            var uploadUrls = new List<List<(int partNumber, string url)>>();
            foreach(var file in response.Files)
            {
                var urls = new List<(int partNumber, string url)>();
                for (int partNumber=1;partNumber<=file.ChunkData.NumberOfParts;partNumber++)
                {
                   
                    var uploadUrl = _communicator.RequestUploadUrl(response.Id, file, partNumber).Result;
                    
                    urls.Add((partNumber: partNumber,url: uploadUrl.Url));
                }
                uploadUrls.Add(urls);               
            }

            //Assert
            //Two files
            Assert.AreEqual(2, uploadUrls.Count);

            //One file has two chuncks as it is larger than 5MB.
            Assert.AreEqual(1, uploadUrls.Count(uu => uu.Count == 2));

            //Check that the upload urls are not empty.
            Assert.IsTrue(uploadUrls.SelectMany(uu => uu.Select(tuple => tuple.url)).All(url => !string.IsNullOrEmpty(url)));

        }

        [TestMethod]
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
            Debug.Print(result.DownloadUrl);
        }
    }
}
