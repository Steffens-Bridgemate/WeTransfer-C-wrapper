using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace WeTransferUploader.V2
{
    public class CommunicatorV2
    {
        // Disabled Logger because it has a hard dependency on NLog
        // private static Logger Logger = LogManager.GetCurrentClassLogger();

        //Clears the token
        public static void ClearToken()
        {
            Properties.Settings.Default.TokenV2 = null;
        }

        // The secret, developer specific, API-key.
        private readonly string ApiKey;

        //The directory where chunks of a file can be created.
        private readonly string ChunkDirectory;

        public CommunicatorV2(string apiKey, string chunkDirectory)
        {
            if (string.IsNullOrEmpty(apiKey))
                throw new ArgumentNullException(nameof(apiKey));
            if (!Directory.Exists(chunkDirectory))
                throw new DirectoryNotFoundException(nameof(chunkDirectory));

            this.ApiKey = apiKey;
            this.ChunkDirectory = chunkDirectory;
        }

        /// <summary>
        /// A Json web token (JWT) that allows creation of transfer objects for a year. The secret API key is needed to obtain one.
        /// The token is stored in de project's properties, together with its creation date.
        /// </summary>
        public static string Token
        {
            get
            {
                var ageOfCurrentToken = DateTime.Now.Subtract(Properties.Settings.Default.TokenV2CreationDate);
                if (ageOfCurrentToken > TimeSpan.FromDays(300))
                {
                    //Logger.Debug("Token is too old and will not be returned.");
                    return null;
                }

                else
                {
                    return Properties.Settings.Default.TokenV2;
                }

            }
            set
            {
                //Logger.Debug($"Token set to: {value}.");
                Properties.Settings.Default.TokenV2 = value;
                Properties.Settings.Default.TokenV2CreationDate = DateTime.Now;
                Properties.Settings.Default.Save();
            }
        }

        /// <summary>
        /// The main entry for the component. Will handle all stages of the upload.
        /// </summary>
        /// <param name="fullPaths">A collection of paths for the transfer</param>
        /// <param name="requestName">The (arbitrary) name for the transfer</param>
        /// <param name="progress">An optional parameter to report back progress to the calling code.</param>
        /// <returns></returns>
        public async Task<UploadResultV2> UploadFiles(IEnumerable<string> fullPaths,
                                                      string requestName,
                                                      string user,
                                                      IProgress<ProgressReportV2> progress = null)
        {
            //Validation
            if (fullPaths == null || fullPaths.Count() == 0)
                throw new ArgumentNullException(nameof(fullPaths));

            foreach (var path in fullPaths)
                if (!File.Exists(path))
                    throw new FileNotFoundException(path);

            if (string.IsNullOrEmpty(user))
                throw new ArgumentNullException(nameof(user));
            //


            UploadResultV2.Stage currentStage = UploadResultV2.Stage.NotSet;
            try
            {
                // If no progress was passed, create an empty one. In this way we do not have to check for null each time we want to report progress.
                if (progress == null) progress = new Progress<ProgressReportV2>();

                //  Use existing token or request a new one.
                currentStage = UploadResultV2.Stage.Token;
                if (string.IsNullOrEmpty(Token))
                {
                    //Logger.Debug("No token. Obtaining new one...");
                    var obtainTokenResult = await GetToken(user);
                    if (!obtainTokenResult.Success.Value)
                        return new UploadResultV2(UploadResultV2.ResultCode.ApiError, currentStage, "Token could not be obtained.");
                    else
                        progress.Report(new ProgressReportV2("New token obtained", 5));
                }
                //

                // Create a new transfer request.
                currentStage = UploadResultV2.Stage.TransferRequest;
                //Logger.Debug("Creating transfer...");

                //Calculate the filesizes in order to report correct progress
                var fileInfos = new List<FileInfo>();
                var fileRequests = new List<FileRequestContent>();
                foreach (var file in fullPaths)
                {
                    var info = new FileInfo(file);
                    fileInfos.Add(info);
                    var fileRequest = new FileRequestContent(info.Name,(int) info.Length,info.FullName);
                    fileRequests.Add(fileRequest);
                }

                var content = new FileUploadRequestContent(requestName, fileRequests.ToArray());

                var createTransferResponse = await CreateTransfer(content);

                if (!createTransferResponse.Success.Value)
                {
                    return new UploadResultV2(UploadResultV2.ResultCode.ApiError, currentStage, "No transfer could be created.");
                }
                else
                    progress.Report(new ProgressReportV2("Transfer created", 10));
                //

                var totalFileSize = fileInfos.Sum(info => info.Length);

                // Add the files to the transfer
                currentStage = UploadResultV2.Stage.AddFiles;

                var currentProgess = 10;
                foreach (var file in createTransferResponse.Files)
                {
                    var addableProgressForThisFile = 90 * ((double)file.ChunkData.ChunkSize*file.ChunkData.NumberOfParts / (double)totalFileSize);
                    var result = await UploadSingleFile(createTransferResponse.Id,
                                                       file,
                                                       progress,
                                                       progressThusFar: currentProgess,
                                                       maxAddableProgress: (int)addableProgressForThisFile);

                    if (result.Result != UploadResultV2.ResultCode.Success)
                        return result;
                    currentProgess += (int)addableProgressForThisFile;
                }

                var completionResponse =await CompleteTransfer(createTransferResponse.Id);
               
                return new UploadResultV2(UploadResultV2.ResultCode.Success,
                                          UploadResultV2.Stage.Complete, "All files uploaded",
                                          completionResponse.DownloadUrl);

            }
            catch (TaskCanceledException ex)
            {
                return new UploadResultV2(UploadResultV2.ResultCode.NoConnection, currentStage, ex.Message);
            }
            catch (Exception ex)
            {
                return new UploadResultV2(UploadResultV2.ResultCode.UnknownError, currentStage, ex.Message);
            }


        }

        internal async Task<UploadResultV2> UploadSingleFile(string transferId,
                                                          SingleFileTransferResponseData singleFileTransferData,
                                                          IProgress<ProgressReportV2> progress,
                                                          int progressThusFar,
                                                          int maxAddableProgress)
        {
            var fullPath = singleFileTransferData.FullPath;
            progress.Report(new ProgressReportV2($"Uploading '{Path.GetFileName(fullPath)}'...", (double)progressThusFar));

            // Split the files into the requested number of chuncks.
            var currentStage = UploadResultV2.Stage.SplitFiles;
            //Logger.Debug("Splitting files...");
            await Task.Run(() => IOUtil.SplitFile(fullPath, singleFileTransferData.ChunkData.ChunkSize, ChunkDirectory));

            progress.Report(new ProgressReportV2("Files split", progressThusFar + maxAddableProgress / 5));
            //

            // Acquire the upload urls for each chunck of each file.
            currentStage = UploadResultV2.Stage.UploadUrl;
            //Logger.Debug("Requesting upload urls...");
            var uploadUrls = new List<FilePartUploadUrlResponse>();
            for (var i = 1; i <= singleFileTransferData.ChunkData.NumberOfParts; i++)
            {
               var partialUploadResponse = await RequestUploadUrl(transferId,singleFileTransferData, partNumber: i);
               if (!partialUploadResponse.Success.Value)
               return new UploadResultV2(UploadResultV2.ResultCode.ApiError, currentStage, $"No upload url could be obtained for part {i}.");
               uploadUrls.Add(partialUploadResponse);
            }

            progress.Report(new ProgressReportV2("Upload urls acquired", progressThusFar + maxAddableProgress / 4));
            //

            progressThusFar += maxAddableProgress / 4;
            maxAddableProgress = maxAddableProgress * 3 / 4;

            // Upload each chunk!
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
                    var progressPercentage = maxAddableProgress * ((double)rp.PartNumber / (double)uploadUrls.Count);
                    progress.Report(new ProgressReportV2($"Part {rp.PartNumber} uploaded", progressThusFar + progressPercentage));
                }

            }
            //

            // Signal to the web API that we are finished.
            currentStage = UploadResultV2.Stage.Complete;
            var completeResponse = await CompleteSingleFileUpload(transferId, singleFileTransferData.Id,uploadUrls.Count);

            if (!completeResponse.Success.Value)
                return new UploadResultV2(UploadResultV2.ResultCode.ApiError, currentStage, completeResponse.Message);
            else
            {
                progress.Report(new ProgressReportV2($"Upload of file '{Path.GetFileName(fullPath)}' completed", progressThusFar + maxAddableProgress));
                return new UploadResultV2(UploadResultV2.ResultCode.Success, UploadResultV2.Stage.Complete, completeResponse.Message);
            }

            //
        }
        /// <summary>
        /// Returns a new token. A token is valid for a year.
        /// </summary>
        /// <returns></returns>
        public async Task<TokenResponseV2> GetToken(string user)
        {
            if (string.IsNullOrEmpty(user))
                throw new ArgumentNullException(nameof(user));

            using (var client = new HttpClient())
            {
                var request = new TokenRequest(ApiKey, new TokenRequestContent(user));

                var tokenResponse = await WaitForResponse<TokenResponseV2>(client, request);
                if (tokenResponse.Success.Value)
                {
                    Token = tokenResponse.Token;
                }
                return tokenResponse;
            }

        }

        /// <summary>
        /// Creates a new, empty transfer object. We will add files later.
        /// </summary>
        /// <param name="creationRequest">A wrapper around the (arbitrary)name for the transfer</param>
        /// <returns></returns>
        internal async Task<TransferRequestResponseV2> CreateTransfer(FileUploadRequestContent fileRequest)
        {
            using (var client = new HttpClient())
            {
                var request = new CreateTransferRequestV2(ApiKey, Token, fileRequest);
          
                var createTransferResponse = await WaitForResponse<TransferRequestResponseV2>(client, request);

                foreach(var file in createTransferResponse.Files)
                {
                    file.FullPath = fileRequest.Files.Single(f => f.Name == file.Name).FullPath;
                }

                return createTransferResponse;
            }
        }

        /// <summary>
        /// Tells the web API which files we want to add.
        /// </summary>
        /// <param name="transferId">The id of the new transfer we created.</param>
        /// <param name="container">A container for the the full paths to the files that we want to upload.</param>
        /// <returns></returns>
        private async Task<AddItemResponse[]> AddTransferItems(string transferId, TransferItemContainer container)
        {
            throw new NotImplementedException();
            //using (var client = new HttpClient())
            //{
            //    var request = CreateRequest($"https://dev.wetransfer.com/v1/transfers/{transferId}/items", HttpMethod.Post);
            //    AddBearerToken(request);

            //    var stringPayload = await Task.Run(() => JsonConvert.SerializeObject(container));
            //    var httpContent = new StringContent(stringPayload, Encoding.UTF8, "application/json");
            //    request.Content = httpContent;

            //    var addedTransferItemResponse = await WaitForResponseArray<AddItemResponse>(client, request);

            //    return addedTransferItemResponse;
            //}
        }

        /// <summary>
        /// Requests an upload url for each chunk of a file.
        /// </summary>
        /// <param name="uploadData">Contains data about the chunk that the web API needs.</param>
        /// <param name="partNumber">The partnumber of the chunk.</param>
        /// <returns></returns>
        internal async Task<FilePartUploadUrlResponse> RequestUploadUrl(string transferId, 
                                                                        SingleFileTransferResponseData fileData,
                                                                        int partNumber)
        {
            using (var client = new HttpClient())
            {
                var request = new FilePartUploadUrlRequestV2(ApiKey, Token, transferId, fileData.Id, partNumber);
                var response = await WaitForResponse<FilePartUploadUrlResponse>(client, request);
                response.PartNumber = partNumber;
                return response;
            }

            
           
        }

        /// <summary>
        /// Performs the actual upload of one chunk of a file.
        /// </summary>
        /// <param name="requestUri">The url where the file must be uploaded to.</param>
        /// <param name="fullPath">The full path to the file</param>
        /// <returns></returns>
        private async Task<PartialUploadResponse> UploadPart(string requestUri, string fullPath)
        {
            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage(HttpMethod.Put, requestUri);
                HttpResponseMessage response;
                using (var stream = File.OpenRead(fullPath))
                {
                    request.Content = new StreamContent(stream);
                    response = await client.SendAsync(request);
                }

                var uploadResponse = new PartialUploadResponse { Success = response.StatusCode == System.Net.HttpStatusCode.OK };

                return uploadResponse;
            }

        }

        /// <summary>
        /// Signals completion of the uploads of one file.
        /// </summary>
        /// <param name="fileId">The id of the file</param>
        /// <returns></returns>
        private async Task<SingleFileUploadCompletedResponse> CompleteSingleFileUpload(string transferId, string fileId,int numberOfParts)
        {
           
            using (var client = new HttpClient())
            {
                var request = new SingleFileUploadCompleteRequestV2(ApiKey, Token, transferId, fileId, numberOfParts);
               
                var completeResponse = await WaitForResponse<SingleFileUploadCompletedResponse>(client, request);
                return completeResponse;
            }

        }

        /// <summary>
        /// Signals completion of the uploads of all files.
        /// </summary>
        /// <param name="fileId">The id of the file</param>
        /// <returns></returns>
        private async Task<TransferCompletedResponse> CompleteTransfer(string transferId)
        {

            using (var client = new HttpClient())
            {
                var request = new TransferCompleteRequestV2(ApiKey, Token, transferId);

                var completeResponse = await WaitForResponse<TransferCompletedResponse>(client, request);
                return completeResponse;
            }

        }

        /// <summary>
        /// Sends the HttpRequestMessage and awaits its response containing a JSON array.
        /// </summary>
        /// <typeparam name="TResponse">The type of response that is expected.</typeparam>
        /// <param name="client">The HttpClient</param>
        /// <param name="request">The HttpRequestMessage</param>
        /// <returns></returns>
        private async Task<TResponse[]> WaitForResponseArray<TResponse>(HttpClient client, HttpRequestMessage request) where TResponse : JsonResponse
        {
            var response = await client.SendAsync(request);
            var result = response.StatusCode;
            var message = await response.Content.ReadAsStringAsync();

            var responses = JsonConvert.DeserializeObject<TResponse[]>(message);

            return responses;
        }

        /// <summary>
        /// Sends the HttpRequestMessage and awaits its response.
        /// </summary>
        /// <typeparam name="TResponse">The type of response that is expected.</typeparam>
        /// <param name="client">The HttpClient</param>
        /// <param name="request">The HttpRequestMessage</param>
        /// <returns></returns>
        private async Task<TResponse> WaitForResponse<TResponse>(HttpClient client, HttpRequestMessage request) where TResponse : JsonResponseV2
        {

            var response = await client.SendAsync(request);
            var result = response.StatusCode;
            var message = await response.Content.ReadAsStringAsync();

            var tResponse = JsonConvert.DeserializeObject<TResponse>(message);
            tResponse.RequestUri = request.RequestUri.ToString();
            tResponse.Message = response.ReasonPhrase;

            if (tResponse.Success == null)
                tResponse.Success = result.HasFlag(HttpStatusCode.OK);
            return tResponse;
        }
    }
 
}
