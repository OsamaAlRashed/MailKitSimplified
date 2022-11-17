global using Xunit;
global using Moq;
global using MimeKit;
global using MailKit;
global using MailKit.Net.Smtp;
using MailKit.Security;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MailKitSimplified.Sender.Services;
using MailKitSimplified.Sender.Models;
using MailKitSimplified.Sender.Abstractions;

namespace MailKitSimplified.Sender.Tests
{
    public class SmtpSenderUnitTests
    {
        private const string _localhost = "localhost";
        private readonly Mock<ISmtpClient> _smtpSenderMock;
        private readonly ISmtpSender _smtpSender;

        public SmtpSenderUnitTests()
        {
            var loggerMock = new Mock<ILogger<SmtpSender>>();
            var protocolLoggerMock = new Mock<IProtocolLogger>();
            _smtpSenderMock = new Mock<ISmtpClient>();
            _smtpSenderMock.Setup(_ => _.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<SecureSocketOptions>(), It.IsAny<CancellationToken>())).Verifiable();
            _smtpSenderMock.Setup(_ => _.AuthenticateAsync(It.IsAny<ICredentials>(), It.IsAny<CancellationToken>())).Verifiable();
            _smtpSenderMock.Setup(_ => _.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>()))
                .ReturnsAsync("Mail accepted").Verifiable();
            var smtpSenderOptions = Options.Create(new EmailSenderOptions(_localhost, new NetworkCredential()));
            _smtpSender = new SmtpSender(smtpSenderOptions, loggerMock.Object, protocolLoggerMock.Object, _smtpSenderMock.Object);
        }

        [Theory]
        [InlineData(_localhost, 25)]
        [InlineData("smtp.example.com", 0)]
        [InlineData("smtp.google.com", 465)]
        [InlineData("smtp.sendgrid.com", 2525)]
        [InlineData("smtp.mail.yahoo.com", 587)]
        [InlineData("outlook.office365.com", ushort.MinValue)]
        [InlineData("smtp.freesmtpservers.com", ushort.MaxValue)]
        public void CreateSmtpSender_WithValidSmtpHostNames_ReturnsSmtpSender(string smtpHost, ushort smtpPort)
        {
            using var smtpSender = SmtpSender.Create(smtpHost, smtpPort);
            Assert.NotNull(smtpSender);
        }

        [Fact]
        public void CreateSmtpSender_WithAnyHostAndCredential_ReturnsSmtpSender()
        {
            using var smtpSender = SmtpSender.Create(_localhost, It.IsAny<NetworkCredential>());
            Assert.NotNull(smtpSender);
        }

        [Fact]
        public void WriteEmail_WithSmtpSender_VerifyNotNull()
        {
            Assert.NotNull(_smtpSender.WriteEmail);
        }

        [Fact]
        public async Task ConnectSmtpClientAsync_VerifyCalls()
        {
            // Act
            var imapClient = await _smtpSender.ConnectSmtpClientAsync(It.IsAny<CancellationToken>());
            // Assert
            Assert.NotNull(imapClient);
            _smtpSenderMock.Verify(_ => _.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<SecureSocketOptions>(), It.IsAny<CancellationToken>()), Times.Once);
            _smtpSenderMock.Verify(_ => _.AuthenticateAsync(It.IsAny<ICredentials>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SendAsync_VerifySentAsync()
        {
            // Act
            await _smtpSender.SendAsync(new MimeMessage(), It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>());
            // Assert
            _smtpSenderMock.Verify(_ => _.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>()), Times.Once);
        }

        [Fact]
        public async Task TrySendAsync_VerifySentAsync()
        {
            var isSent = await _smtpSender.TrySendAsync(new MimeMessage(), It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>());
            Assert.True(isSent);
            _smtpSenderMock.Verify(_ => _.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>()), Times.Once);
        }

        [Fact]
        public void ValidateEmailAddresses_WithValidEmails_VerifyValid()
        {
            var source = new string[] { "from@localhost" };
            var destination = new string[] { "to@localhost" };
            var valid = SmtpSender.ValidateEmailAddresses(source, destination, NullLogger.Instance);
            Assert.True(valid);
        }

        [Fact]
        public void ValidateEmailAddresses_WithInvalidEmails_VerifyInvalid()
        {
            var source = new string[] { "from@localhost", "admin@localhost", "me" };
            var destination = new string[] { "to@localhost", "admin@localhost", "you" };
            var valid = SmtpSender.ValidateEmailAddresses(source, destination, NullLogger.Instance);
            Assert.False(valid);
        }

        [Fact]
        public void ValidateEmailAddresses_WithNoEmails_VerifyInvalid()
        {
            var valid = SmtpSender.ValidateEmailAddresses(new string[] { }, new string[] { }, NullLogger.Instance);
            Assert.False(valid);
        }

        [Fact]
        public void ToString_Verify()
        {
            var serialised = _smtpSender.ToString();
            Assert.Contains(_localhost, serialised);
        }
    }
}