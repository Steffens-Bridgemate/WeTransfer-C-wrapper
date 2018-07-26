using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
//using NLog;
using System.Net;

namespace WeTransferUploader
{
    public class Communicator
    {
        // Disabled Logger because it has a hard dependency on NLog
        // private static Logger Logger = LogManager.GetCurrentClassLogger();

        // The secret, developer specific, API-key.
        private readonly string ApiKey ;

        //The directory where chunks of a file can be created.
        private readonly string ChunkDirectory;

        //The exact size that all chuncks except the last one must have.
        private readonly int ChunkSize = 6291456;

        public Communicator(string apiKey,string chunkDirectory)
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
                var ageOfCurrentToken = DateTime.Now.Subtract(Properties.Settings.Default.TokenCreationDate);
                if ( ageOfCurrentToken> TimeSpan.FromDays(300))
                {
                    //Logger.Debug("Token is too old and will not be returned.");
                    return null;
                }
                  
                else
                {
                    return Properties.Settings.Default.Token;
                }
                    
            }
            set
            {
                //Logger.Debug($"Token set to: {value}.");
                Properties.Settings.Default.Token = value;
                Properties.Settings.Default.TokenCreationDate = DateTime.Now;
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
        public async Task< UploadResult> UploadFiles(IEnumerable<string> fullPaths,
                                                     string requestName,
                                                     IProgress<ProgressReport> progress=null)
        {
            //Validation
            if (fullPaths == null || fullPaths.Count() == 0)
                throw new ArgumentNullException(nameof(fullPaths));

            foreach (var path in fullPaths)
                if (!File.Exists(path))
                    throw new FileNotFoundException(path);
            //


            UploadResult.Stage currentStage=UploadResult.Stage.NotSet;
            try
            {
                // If no progress was passed, create an empty one. In this way we do not have to check for null each time we want to report progress.
                if (progress == null) progress = new Progress<ProgressReport>();

                //  Use exisiting token or request a new one.
                currentStage = UploadResult.Stage.Token;
                if (string.IsNullOrEmpty(Token))
                {
                    //Logger.Debug("No token. Obtaining new one...");
                    var obtainTokenResult = await GetToken();
                    if (!obtainTokenResult.Success.Value)
                        return new UploadResult(UploadResult.ResultCode.ApiError, currentStage, "Token could not be obtained.");
                    else
                        progress.Report(new ProgressReport("New token obtained", 5));
                }
                //

                // Create a new transfer request.
                currentStage = UploadResult.Stage.TransferRequest;
                //Logger.Debug("Creating transfer...");
                var createTransferResponse = await CreateTransfer(new CreateTransferRequest(requestName));

                if (!createTransferResponse.Success.Value)
                {
                    return new UploadResult(UploadResult.ResultCode.ApiError, currentStage, "No transfer could be created.");
                }
                else
                    progress.Report(new ProgressReport("Transfer created", 10));
                //

                //Calculate the filesizes in order to report correct progress
                var fileInfos = new List<FileInfo>();
                foreach(var file in fullPaths)
                {
                    var info = new FileInfo(file);
                    fileInfos.Add(info);
                }

                var totalFileSize = fileInfos.Sum(info=>info.Length);
              
                // Add the files to the transfer
                currentStage = UploadResult.Stage.AddFiles;

                var currentProgess = 10;
                foreach(var file in fileInfos)
                {
                   var addableProgressForThisFile =90 * ((double)file.Length /(double) totalFileSize);
                   var result=await  UploadSingleFile(file.FullName,
                                                      createTransferResponse,
                                                      progress,
                                                      progressThusFar:currentProgess,
                                                      maxAddableProgress:(int)addableProgressForThisFile);
                    if (result.Result != UploadResult.ResultCode.Success)
                        return result;
                    currentProgess +=(int) addableProgressForThisFile;
                }

                return new UploadResult(UploadResult.ResultCode.Success, 
                                        UploadResult.Stage.Complete, "All files uploaded", 
                                        createTransferResponse.ShortenedUrl);

            }
            catch (TaskCanceledException ex)
            {
                return new UploadResult(UploadResult.ResultCode.NoConnection, currentStage, ex.Message);
            }
            catch(Exception ex)
            {
                return new UploadResult(UploadResult.ResultCode.UnknownError, currentStage, ex.Message);
            }
                
 
        }

        private async Task<UploadResult> UploadSingleFile(string singleFile, CreateTransferResponse transferData,
                                                          IProgress<ProgressReport> progress,
                                                          int progressThusFar,
                                                          int maxAddableProgress)
        {
            progress.Report(new ProgressReport($"Uploading '{Path.GetFileName(singleFile)}'...",(double)progressThusFar));

            // Add the item to the transfer.
            var item = new TransferItem(singleFile);
            var addItemResponse = (await AddTransferItems(transferData.Id, new TransferItemContainer(new[] { item }))).Single();

            progress.Report(new ProgressReport("Files added", progressThusFar+maxAddableProgress/10));
            //

            // Split the files into the requested number of chuncks.
            var currentStage = UploadResult.Stage.SplitFiles;
            //Logger.Debug("Splitting files...");
            await Task.Run(() => IOUtil.SplitFile(item.FullPath(), ChunkSize, ChunkDirectory));

            progress.Report(new ProgressReport("Files split", progressThusFar+maxAddableProgress/5));
            //

            // Acquire the upload urls for each chunck of each file.
            currentStage = UploadResult.Stage.UploadUrl;
            //Logger.Debug("Requesting upload urls...");
            var uploadRequests = new List<PartialUploadResponse>();
            for (var i = 1; i <= addItemResponse.Meta.MultipartParts; i++)
            {
                var partialUploadRequest = await RequestUploadUrl(addItemResponse, partNumber: i);
                if (!partialUploadRequest.Success.Value)
                    return new UploadResult(UploadResult.ResultCode.ApiError, currentStage, $"No upload url could be obtained for part {i}.");
                uploadRequests.Add(partialUploadRequest);
            }

            progress.Report(new ProgressReport("Upload urls acquired", progressThusFar+maxAddableProgress/4));
            //

            progressThusFar += maxAddableProgress / 4;
            maxAddableProgress =maxAddableProgress*3/4;

            // Upload each chunk!
            currentStage = UploadResult.Stage.Upload;
            //Logger.Debug("Uploading");
            foreach (var rq in uploadRequests)
            {
                var chunkPath = Path.Combine(ChunkDirectory, rq.PartNumber.ToString());
                var uploadResponse = await UploadPart(rq.UploadUrl, chunkPath);
                if (!uploadResponse.Success.Value)
                    return new UploadResult(UploadResult.ResultCode.ApiError, currentStage, $"Part {rq.PartNumber} could not be uploaded.");
                else
                {
                    var progressPercentage = maxAddableProgress * ((double)rq.PartNumber /(double) uploadRequests.Count);
                    progress.Report(new ProgressReport($"Part {rq.PartNumber} uploaded", progressThusFar + progressPercentage));
                }
                
            }
            //

            // Signal to the web API that we are finished.
            currentStage = UploadResult.Stage.Complete;
            var completeResponse = await CompleteUpload(addItemResponse.Id);

            if (!completeResponse.Success.Value)
                return new UploadResult(UploadResult.ResultCode.ApiError, currentStage, completeResponse.Message);
            else
            {
                progress.Report(new ProgressReport($"Upload of file '{Path.GetFileName(singleFile)}' completed", progressThusFar+maxAddableProgress));
                return new UploadResult(UploadResult.ResultCode.Success, UploadResult.Stage.Complete, completeResponse.Message);
            }
             
            //
        }
        /// <summary>
        /// Returns a new token. A token is valid for a year.
        /// </summary>
        /// <returns></returns>
        private async Task<TokenResponse> GetToken()
        {
            using (var client = new HttpClient())
            {
                var request = CreateRequest("https://dev.wetransfer.com/v1/authorize", HttpMethod.Post);

                var tokenResponse = await WaitForResponse<TokenResponse>(client, request);
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
        private async Task< CreateTransferResponse> CreateTransfer(CreateTransferRequest creationRequest)
        {
            using (var client = new HttpClient())
            {
                var request = CreateRequest("https://dev.wetransfer.com/v1/transfers", HttpMethod.Post);
                AddBearerToken(request);

                // Serialize our concrete class into a JSON String
                var stringPayload = await Task.Run(() => JsonConvert.SerializeObject(creationRequest));

                // Wrap our JSON inside a StringContent which then can be used by the HttpClient class
                var httpContent = new StringContent(stringPayload, Encoding.UTF8, "application/json");
                request.Content = httpContent;

                var createTransferResponse = await WaitForResponse<CreateTransferResponse>(client, request);

                return createTransferResponse;
            }
        }

        /// <summary>
        /// Tells the web API which files we want to add.
        /// </summary>
        /// <param name="transferId">The id of the new transfer we created.</param>
        /// <param name="container">A container for the the full paths to the files that we want to upload.</param>
        /// <returns></returns>
        private async Task<AddItemResponse[]> AddTransferItems(string transferId,TransferItemContainer container)
        {
            using (var client = new HttpClient())
            {
                var request = CreateRequest($"https://dev.wetransfer.com/v1/transfers/{transferId}/items", HttpMethod.Post);
                AddBearerToken(request);

                var stringPayload = await Task.Run(() => JsonConvert.SerializeObject(container));
                var httpContent = new StringContent(stringPayload, Encoding.UTF8, "application/json");
                request.Content = httpContent;

                var addedTransferItemResponse = await WaitForResponseArray<AddItemResponse>(client, request);

                return addedTransferItemResponse;
            }
        }

        /// <summary>
        /// Requests an upload url for each chunk of a file.
        /// </summary>
        /// <param name="uploadData">Contains data about the chunk that the web API needs.</param>
        /// <param name="partNumber">The partnumber of the chunk.</param>
        /// <returns></returns>
        private async Task<PartialUploadResponse> RequestUploadUrl(AddItemResponse uploadData,int partNumber)
        {
            using (var client = new HttpClient())
            {
                var requestUrl = $"https://dev.wetransfer.com/v1/files/{uploadData.Id}/uploads/{partNumber}/{uploadData.Meta.MultipartUploadId}";
                var request = CreateRequest(requestUrl, HttpMethod.Get);
                AddBearerToken(request);

                var partialUploadResponse = await WaitForResponse<PartialUploadResponse>(client, request);

                return partialUploadResponse;
            }
        }

        /// <summary>
        /// Performs the actual upload of one chunk of a file.
        /// </summary>
        /// <param name="requestUri">The url where the file must be uploaded to.</param>
        /// <param name="fullPath">The full path to the file</param>
        /// <returns></returns>
        private async Task<PartialUploadResponse> UploadPart(string requestUri,string fullPath)
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
        private async Task<CompleteResponse> CompleteUpload(string fileId)
        {
            using (var client = new HttpClient())
            {
                var request = CreateRequest($"https://dev.wetransfer.com/v1/files/{fileId}/uploads/complete", HttpMethod.Post);
                AddBearerToken(request);

                var completeResponse = await WaitForResponse<CompleteResponse>(client, request);
                return completeResponse;
            }
        
        }

        /// <summary>
        /// Creates the fixed part of the HttpRequestMessage
        /// </summary>
        /// <param name="requestUri">The url that the request must be sent to.</param>
        /// <param name="method">The HttpMethod: Post, Get, Put</param>
        /// <returns></returns>
        private HttpRequestMessage CreateRequest(string requestUri,HttpMethod method)
        {

            var request = new HttpRequestMessage();
            request.Method = method;
            request.RequestUri = new Uri(requestUri);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("x-api-key", ApiKey);
            return request;
        }

        /// <summary>
        /// Adds a Header with the token to the fixed part of the HttpRequestMessage.
        /// </summary>
        /// <param name="request"></param>
        private void AddBearerToken(HttpRequestMessage request)
        {
            request.Headers.Add("Authorization",$"Bearer {Token}");
        }

        /// <summary>
        /// Sends the HttpRequestMessage and awaits its response containing a JSON array.
        /// </summary>
        /// <typeparam name="TResponse">The type of response that is expected.</typeparam>
        /// <param name="client">The HttpClient</param>
        /// <param name="request">The HttpRequestMessage</param>
        /// <returns></returns>
        private async Task<TResponse[]> WaitForResponseArray<TResponse>(HttpClient client, HttpRequestMessage request) where TResponse: JsonResponse
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
        private async Task<TResponse> WaitForResponse<TResponse>(HttpClient client,HttpRequestMessage request) where TResponse: JsonResponse
        {
      
            var response = await client.SendAsync(request);
            var result = response.StatusCode;
            var message = await response.Content.ReadAsStringAsync();

            var tResponse  = JsonConvert.DeserializeObject<TResponse>(message);
            tResponse.RequestUri = request.RequestUri.ToString();

            if (tResponse.Success == null)
                tResponse.Success = result.HasFlag(HttpStatusCode.OK);
            return tResponse;
        }
    }

 

    public class MultiPartItemDescription
    {
        [JsonProperty("multipart_parts")]
        public int MultipartParts { get; set; }

        [JsonProperty("multipart_upload_id")]
        public string MultipartUploadId { get; set; }
    }

   
    public class UploadResult
    {

        public UploadResult(ResultCode code, Stage stage,string message,string downLoadUrl="")
        {
            this.Result = code;
            this.CurrentStage = stage;
            this.Message = message;
            this.DownloadUrl = downLoadUrl;
        }

        public enum ResultCode
        {
            NotSet,
            Success,
            NoConnection,
            ApiError,
            UnknownError
        }
        public enum Stage
        {
            NotSet,
            Token,
            TransferRequest,
            AddFiles,
            SplitFiles,
            UploadUrl,
            Upload,
            Complete
        }

        public ResultCode Result { get; }
        public Stage CurrentStage { get; }
        public string Message { get; }

        public string DownloadUrl { get; }

    }

    public class ProgressReport
    {

        public ProgressReport(string message, double percentage)
        {
            this.Message = message;
            this.Percentage = percentage;
        }

        public string Message { get; }
        public double Percentage { get; }
    }

}
