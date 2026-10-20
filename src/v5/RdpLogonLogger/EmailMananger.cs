using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace RdpLogonLogger
{
    public static class EmailMananger
    {
        public  static void SendAlertNotification(string server,string actionPerformed,bool enabledCc,List<Attachment> attachments)
        {
            var settings = new EmailSettings();
            var ccs = new Dictionary<int, string>
            {
                { 1, "joshua.shepherd@ncagr.gov" }
                //{ 2, "talley.ouro@ncagr.gov" }
            };

            using (var smtp = new SmtpClient())
            {
                smtp.Credentials = new NetworkCredential(settings.UserName, settings.Password);
                smtp.EnableSsl = settings.UseSSL;
                smtp.Host = settings.Server;

                using (var mailMessage = new MailMessage())
                {
                    if (enabledCc)
                    {
                        mailMessage.CC.Add(ccs[1]);
                        mailMessage.CC.Add(ccs[2]);
                    }
                   
                    mailMessage.Body = actionPerformed;
                    if (attachments.Any())
                    {
                        foreach (var att in attachments)
                        {
                            mailMessage.Attachments.Add(att);
                        }
                    }

                    mailMessage.From = new MailAddress(settings.SenderEmailAddress);
                    mailMessage.To.Add(new MailAddress(settings.SupportEmailAddresses[1]));

                    mailMessage.IsBodyHtml=true;
                    mailMessage.Subject = $"Remote Desktop Connection Performed on Server:{server}";

                    smtp.Send(mailMessage);
                }

              
            }
        }
    }

    public class EmailSettings
    {
        public string Server { get; set; } = "outbound.mail.nc.gov";
        public int Port { get; set; } = 25;
        public string SenderEmailAddress { get; set; } = "NCVDL@ncagr.gov";
        public string SenderName { get; set; } = "NCVDL";
        public bool UseSSL { get; set; } = false;
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";

        public string Credentials { get; set; } = "";
        public Dictionary<int,string> SupportEmailAddresses
        {
            get;
            set;
        }
    }
}
