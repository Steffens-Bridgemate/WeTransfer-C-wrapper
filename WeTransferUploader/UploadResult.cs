//using NLog;

namespace WeTransferUploader
{
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

}
