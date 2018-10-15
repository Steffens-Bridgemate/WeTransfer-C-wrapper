using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WeTransferUploader;
using WeTransferUploader.V2;

namespace V2Tests
{
    [TestClass]
    public class V2BoardApiTests
    {
        private const string ApiKey = null; //Fill in your own Api-Key
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
           _user = "BoardApiTester";
            var chunkDirectory = Path.Combine(_appPath, "Chunks");
           _communicator = new CommunicatorV2(ApiKey, chunkDirectory);
            if (CommunicatorV2.Token == null)
                _communicator.GetToken(_user).Wait();
        }
        [TestMethod]
        public void CreateBoard()
        {
            //Act
            var response = _communicator.CreateBoard(_user).Result;

            //Assert
            Assert.IsTrue(response.Success.Value);
            Assert.IsFalse(string.IsNullOrWhiteSpace(response.BoardUrl));
            Debug.Print(response.BoardUrl);
            Debug.Print(response.Id);
        }

        [TestMethod]
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

        [TestMethod]
        public void AddTwoFilesToBoard()
        {
            //Arrange
            CommunicatorV2.ClearToken();
            _communicator.GetToken("André Steffens").Wait();
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
            var fileUploadResponse = _communicator.UploadFilesToBoard(boardResponse.Id, infos).Result;

            //Assert
            Assert.AreEqual(UploadResultV2.ResultCode.Success, fileUploadResponse.Result);

            //Act: get board information
            var infoResponse = _communicator.GetBoardInfo(boardResponse.Id).Result;
            

        }
    }
}
