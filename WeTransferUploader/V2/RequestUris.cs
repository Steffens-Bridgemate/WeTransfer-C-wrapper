using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeTransferUploader.V2
{
    public static class RequestUrisV2
    {
        public static string Token = "https://dev.wetransfer.com/v2/authorize";
        public static string CreateTransfer = "https://dev.wetransfer.com/v2/transfers";
        public static string FilePartUploadUrl = "https://dev.wetransfer.com/v2/transfers/{0}/files/{1}/upload-url/{2}";
        public static string FileUploadComplete = "https://dev.wetransfer.com/v2/transfers/{0}/files/{1}/upload-complete";
        public static string TransferComplete= "https://dev.wetransfer.com/v2/transfers/{0}/finalize" ;
    }
}
