using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace WeTransferUploader.V2
{
    public class TransferApiCommunicator: WeTransferCommunicator
    {
        // Disabled Logger because it has a hard dependency on NLog
        // private static Logger Logger = LogManager.GetCurrentClassLogger();

     /// <summary>
     /// Initializes the class.
     /// </summary>
     /// <param name="apiKey">Your own api-key. Get one from WeTransfer</param>
     /// <param name="chunkDirectory">The directory where files can be split into chunks.</param>
        public TransferApiCommunicator(string apiKey, string chunkDirectory)
        {
            if (string.IsNullOrEmpty(apiKey))
                throw new ArgumentNullException(nameof(apiKey));
            if (!Directory.Exists(chunkDirectory))
                throw new DirectoryNotFoundException(nameof(chunkDirectory));

            this.ApiKey = apiKey;
            this.ChunkDirectory = chunkDirectory;
        }

        /// <summary>
        /// The main entry for the component. Will handle all stages of the upload.
        /// </summary>
        /// <param name="fullPaths">A collection of paths for the transfer</param>
        /// <param name="requestName">The (arbitrary) name for the transfer</param>
        /// <param name="user">The name of the user that will obtain the token. Is mainly iseful for creating Boards.</param>
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

                //Create the data for the files that must be uploaded.
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

        /// <summary>
        /// Uploads a single file by requesting upload urls for each chunk, splitting the file according to the specified chunk size, uploading each chunk and signaling to the API that the file upload is complete.
        /// </summary>
        /// <param name="transferId">The id of the entire tranfser</param>
        /// <param name="singleFileTransferData">Data for the file that must be uploaded</param>
        /// <param name="progress">Used to report back progress</param>
        /// <param name="progressThusFar">Used to report back progress</param>
        /// <param name="maxAddableProgress">Used to report back progress</param>
        /// <returns></returns>
        internal async Task<UploadResultV2> UploadSingleFile(string transferId,
                                                          SingleFileTransferResponseData singleFileTransferData,
                                                          IProgress<ProgressReportV2> progress,
                                                          int progressThusFar,
                                                          int maxAddableProgress)
        {
            var fullPath = singleFileTransferData.FullPath;
            progress.Report(new ProgressReportV2($"Uploading '{Path.GetFileName(fullPath)}'...", (double)progressThusFar));

            // Split the files into the requested number of chuncks. The API has sent info on what chunksize to use when the transfer request was created.
            var currentStage = UploadResultV2.Stage.SplitFiles;
            //Logger.Debug("Splitting files...");
            await Task.Run(() => IOUtil.SplitFile(fullPath, singleFileTransferData.ChunkData.ChunkSize, ChunkDirectory));

            progress.Report(new ProgressReportV2("Files split", progressThusFar + maxAddableProgress / 5));
            //

            // Acquire the upload urls for each chunck of each file.
            currentStage = UploadResultV2.Stage.UploadUrl;
            //Logger.Debug("Requesting upload urls...");

            //Store the upload urls for each chunk.
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
                    var progressPercentage = maxAddableProgress * ((double)rp.PartNumber / (double)uploadUrls.Count);
                    progress.Report(new ProgressReportV2($"Part {rp.PartNumber} uploaded", progressThusFar + progressPercentage));
                }

            }
            //

            // Signal to the web API that we have finished.
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
        /// Creates a new transfer. The files to upload must be specified up front.
        /// </summary>
        /// <param name="fileRequest">A class containing data on the global transfer as well as on the separate files to upload.</param>
        /// <returns></returns>
        /// <remarks>The full paths to the files are added to the API's response in order to keep track of them.
        ///          Note: currently the implementation will fail when two files have the same name, but are located in different directories!</remarks>
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
        /// Requests an upload url for each chunk of a file.
        /// </summary>
        /// <param name="transferId">The id of the global transfer.</param>
        /// <param name="fileData">A class containing data on the file to be uploaded.</param>
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
        internal async Task<TransferCompletedResponse> CompleteTransfer(string transferId)
        {

            using (var client = new HttpClient())
            {
                var request = new TransferCompleteRequestV2(ApiKey, Token, transferId);

                var completeResponse = await WaitForResponse<TransferCompletedResponse>(client, request);
                return completeResponse;
            }

        }

    }

}
