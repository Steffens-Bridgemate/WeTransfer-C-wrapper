//using NLog;

namespace WeTransferUploader
{
    public class ProgressReportV2
    {

        public ProgressReportV2(string message, double percentage)
        {
            this.Message = message;
            this.Percentage = percentage;
        }

        public string Message { get; }
        public double Percentage { get; }
    }

}
