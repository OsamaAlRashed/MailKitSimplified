﻿using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MailKitSimplified.Sender.Models;

namespace MailKitSimplified.Sender.Abstractions
{
    public interface IEmail
    {
        EmailContact From { get; set; }
        IList<EmailContact> To { get; set; }
        IList<string> AttachmentFilePaths { get; set; }
        string Subject { get; set; }
        string Body { get; set; }
        bool IsHtml { get; set; }
        IEmail Write(string fromAddress, string toAddress, string subject = "", string body = "", bool isHtml = true, params string[] attachmentFilePaths);
        Task<bool> SendAsync(CancellationToken token = default);
    }
}
