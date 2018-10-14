//using NLog;

namespace WeTransferUploader
{
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
