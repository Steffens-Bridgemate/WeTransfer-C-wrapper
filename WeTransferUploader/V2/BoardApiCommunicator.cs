using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace WeTransferUploader.V2
{
    public class BoardApiCommunicator : WeTransferCommunicator
    {
        // Disabled Logger because it has a hard dependency on NLog
        // private static Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Initializes the class.
        /// </summary>
        /// <param name="apiKey">Your own api-key. Get one from WeTransfer</param>
        /// <param name="chunkDirectory">The directory where files can be split into chunks.</param>
        public BoardApiCommunicator(string apiKey, string chunkDirectory)
        {
            if (string.IsNullOrEmpty(apiKey))
                throw new ArgumentNullException(nameof(apiKey));
            if (!Directory.Exists(chunkDirectory))
                throw new DirectoryNotFoundException(nameof(chunkDirectory));

            this.ApiKey = apiKey;
            this.ChunkDirectory = chunkDirectory;
        }

        public async Task<BoardCreatedResponse> CreateBoard(string name, string description = null)
        {
            using (var client = new HttpClient())
            {
                var request = new CreateBoardRequest(ApiKey, Token, name, description);
                var response = await WaitForResponse<BoardCreatedResponse>(client, request);
                return response;
            }
        }

        public async Task<BoardInfoResponse> GetBoardInfo(string boardId)
        {
            using (var client = new HttpClient())
            {
                var request = new GetBoardInfoRequest(ApiKey, Token, boardId);
                var response = await WaitForResponse<BoardInfoResponse>(client, request);
                return response;
            }
        }

        public async Task<(HttpStatusCode statusCode, string statusMessage, AddLinkResponse[] responseArray)>
            AddLinks(string boardId, IEnumerable<(string url, string title)> links)
        {
            if (links == null)
                return (statusCode: HttpStatusCode.NoContent,
                        statusMessage: "Please provide content",
                        responseArray: new List<AddLinkResponse>().ToArray());


            using (var client = new HttpClient())
            {
                var request = new AddLinksRequest(ApiKey, Token, boardId, links.ToList());
                var response = await WaitForResponseArray<AddLinkResponse>(client, request);
                return (statusCode: response.statusCode,
                        statusMessage: response.statusMessage,
                        responseArray: response.responseArray);
            }
        }

        public async Task<UploadResultV2> UploadFilesToBoard(string boardId, IEnumerable<string> fullPaths,string user)
        {
            //Validation
            if (string.IsNullOrEmpty(boardId))
                throw new ArgumentNullException(nameof(boardId));

            if (fullPaths == null || fullPaths.Count() == 0)
                throw new ArgumentNullException(nameof(fullPaths));

            foreach (var path in fullPaths)
                if (!File.Exists(path))
                    throw new FileNotFoundException(path);

            if (string.IsNullOrEmpty(user))
                throw new ArgumentNullException(nameof(user));
            //

            var currentStage = UploadResultV2.Stage.Token;
            if (string.IsNullOrEmpty(Token))
            {
                //Logger.Debug("No token. Obtaining new one...");
                var obtainTokenResult = await GetToken(user);
                if (!obtainTokenResult.Success.Value)
                    return new UploadResultV2(UploadResultV2.ResultCode.ApiError, currentStage, "Token could not be obtained.");
                else
                {
                    // progress.Report(new ProgressReportV2("New token obtained", 5));
                }
            }
            var fileRequests = new List<(string name, int size, string fullPath)>();
            foreach (string fullPath in fullPaths)
            {
                var info = new FileInfo(fullPath);
                fileRequests.Add((name: info.Name, size: (int)info.Length, fullPath: info.FullName));
            }

            var uploadRequestResponse = await RequestBoardFileUploadData(boardId, fileRequests);

            foreach (var file in uploadRequestResponse.responseArray)
            {
                var fileUploadResponse = await UploadSingleFileToBoard(boardId, file);
                if (fileUploadResponse.Result != UploadResultV2.ResultCode.Success)
                    return fileUploadResponse;
            }

            return new UploadResultV2(UploadResultV2.ResultCode.Success, UploadResultV2.Stage.Complete, "");
        }

        internal async Task<(HttpStatusCode statusCode, string statusMessage, SingleFileTransferResponseData[] responseArray)>
           RequestBoardFileUploadData(string boardId, IEnumerable<(string name, int size, string fullPath)> files)
        {
            if (files == null)
                return (statusCode: HttpStatusCode.NoContent,
                        statusMessage: "Please provide content",
                        responseArray: new List<SingleFileTransferResponseData>().ToArray());


            using (var client = new HttpClient())
            {
                var request = new AddFilesRequest(ApiKey, Token, boardId, files.ToList());
                var response = await WaitForResponseArray<SingleFileTransferResponseData>(client, request);
                if (response.statusCode == HttpStatusCode.Created)
                {
                    foreach (var fileResponse in response.responseArray)
                        fileResponse.FullPath = files.Single(file => file.name == fileResponse.Name).fullPath;
                }

                return (statusCode: response.statusCode,
                        statusMessage: response.statusMessage,
                        responseArray: response.responseArray);
            }
        }

        internal async Task<UploadResultV2> UploadSingleFileToBoard(string boardId, SingleFileTransferResponseData fileData)
        {
            var fullPath = fileData.FullPath;
            //progress.Report(new ProgressReportV2($"Uploading '{Path.GetFileName(fullPath)}'...", (double)progressThusFar));

            // Split the files into the requested number of chuncks. The API has sent info on what chunksize to use when the transfer request was created.
            var currentStage = UploadResultV2.Stage.SplitFiles;
            //Logger.Debug("Splitting files...");
            await Task.Run(() => IOUtil.SplitFile(fullPath, fileData.ChunkData.ChunkSize, ChunkDirectory));

            // progress.Report(new ProgressReportV2("Files split", progressThusFar + maxAddableProgress / 5));
            //

            // Acquire the upload urls for each chunck of each file.
            currentStage = UploadResultV2.Stage.UploadUrl;
            //Logger.Debug("Requesting upload urls...");

            //Store the upload urls for each chunk.
            var uploadUrls = new List<FilePartUploadUrlResponse>();
            for (var i = 1; i <= fileData.ChunkData.NumberOfParts; i++)
            {
                var partialUploadResponse = await RequestUploadUrlForBoard(boardId, fileData, partNumber: i);
                if (!partialUploadResponse.Success.Value)
                    return new UploadResultV2(UploadResultV2.ResultCode.ApiError, currentStage, $"No upload url could be obtained for part {i}.");
                uploadUrls.Add(partialUploadResponse);
            }

            // progress.Report(new ProgressReportV2("Upload urls acquired", progressThusFar + maxAddableProgress / 4));
            //

            //progressThusFar += maxAddableProgress / 4;
            //maxAddableProgress = maxAddableProgress * 3 / 4;

            // Upload each chunk.
            currentStage = UploadResultV2.Stage.Upload;
            //Logger.Debug("Uploading");
            foreach (var rp in uploadUrls)
            {
                var chunkPath = Path.Combine(ChunkDirectory, rp.PartNumber.ToString());
                var uploadResponse = await UploadPart(rp.Url, chunkPath);
                if (!uploadResponse.Success.Value)
                    return new UploadResultV2(UploadResultV2.ResultCode.ApiError, currentStage, $"Part {rp.PartNumber} could not be uploaded.");
                else
                {
                    //var progressPercentage = maxAddableProgress * ((double)rp.PartNumber / (double)uploadUrls.Count);
                    //progress.Report(new ProgressReportV2($"Part {rp.PartNumber} uploaded", progressThusFar + progressPercentage));
                }

            }
            //

            // Signal to the web API that we have finished.
            currentStage = UploadResultV2.Stage.Complete;
            var completeResponse = await CompleteBoardFileUpload(boardId, fileData.Id);

            if (!completeResponse.Success.Value)
                return new UploadResultV2(UploadResultV2.ResultCode.ApiError, currentStage, completeResponse.Message);
            else
            {
                //progress.Report(new ProgressReportV2($"Upload of file '{Path.GetFileName(fullPath)}' completed", progressThusFar + maxAddableProgress));
                return new UploadResultV2(UploadResultV2.ResultCode.Success, UploadResultV2.Stage.Complete, completeResponse.Message);
            }

        }

        internal async Task<FilePartUploadUrlResponse> RequestUploadUrlForBoard(string boardId,
                                                                                SingleFileTransferResponseData fileData,
                                                                                int partNumber)
        {
            using (var client = new HttpClient())
            {
                var request = new BoardFilePartUploadUrlRequestV2(ApiKey,
                                                                  Token,
                                                                  boardId,
                                                                  fileData.Id,
                                                                  fileData.ChunkData.Id,
                                                                  partNumber);
                var response = await WaitForResponse<FilePartUploadUrlResponse>(client, request);
                response.PartNumber = partNumber;
                return response;
            }

        }

        /// <summary>
        /// Signals completion of the uploads of one file.
        /// </summary>
        /// <param name="fileId">The id of the file</param>
        /// <returns></returns>
        private async Task<SingleFileUploadCompletedResponse> CompleteBoardFileUpload(string boardId, string fileId)
        {

            using (var client = new HttpClient())
            {
                var request = new BoardFileUploadCompleteRequestV2(ApiKey, Token, boardId, fileId);

                var completeResponse = await WaitForResponse<SingleFileUploadCompletedResponse>(client, request);
                return completeResponse;
            }

        }
    }

}
