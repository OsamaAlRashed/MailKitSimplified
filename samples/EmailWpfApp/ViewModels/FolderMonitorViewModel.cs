﻿using MailKit;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using MailKitSimplified.Receiver.Abstractions;
using MailKitSimplified.Receiver.Extensions;
using EmailWpfApp.Extensions;
using EmailWpfApp.Models;

namespace EmailWpfApp.ViewModels
{
    public sealed partial class FolderMonitorViewModel : BaseViewModel, IDisposable
    {
        private readonly Channel<IMessageSummary> _queue;

        public ObservableCollection<string> ViewModelItems { get; private set; } = new() { _inbox };
        public string SelectedViewModelItem { get; set; } = _inbox;

        public ObservableCollection<Email> ViewModelDataGrid { get; private set; } = new();

        [ObservableProperty]
        private Email selectedEmail = new();

        [ObservableProperty]
        private string imapHost = "localhost";

        [ObservableProperty]
        private bool isInProgress;

        [ObservableProperty]
        private int progressBarPercentage;

        [ObservableProperty]
        private string _messageTextBlock = string.Empty;

        private static readonly string _inbox = "INBOX";
        private readonly CancellationTokenSource _cts = new();
        private readonly BackgroundWorker _worker = new();
        //private readonly EmailDbContext? _dbContext;
        private readonly IImapReceiver _imapReceiver;
        private readonly ILogger _logger;

        public FolderMonitorViewModel() : base()
        {
            _imapReceiver = Ioc.Default.GetRequiredService<IImapReceiver>();
            _logger = Ioc.Default.GetRequiredService<ILogger<FolderMonitorViewModel>>();
            //_dbContext = Ioc.Default.GetService<EmailDbContext>();
            StatusText = string.Empty;

            int capacity = 0;
            if (capacity > 0)
            {
                var channelOptions = new BoundedChannelOptions(capacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = true,
                    SingleReader = true,
                    SingleWriter = true
                };
                _queue = Channel.CreateBounded<IMessageSummary>(channelOptions);
            }
            else
            {
                var channelOptions = new UnboundedChannelOptions()
                {
                    AllowSynchronousContinuations = true,
                    SingleReader = true,
                    SingleWriter = true
                };
                _queue = Channel.CreateUnbounded<IMessageSummary>(channelOptions);
            }

        }

        [RelayCommand]
        private async Task ConnectHostAsync()
        {
            IsInProgress = true;
            try
            {
                StatusText = "Getting mail folder names...";
                await GetMailFolderNamesAsync().ConfigureAwait(false);
                StatusText = $"Connected to {ImapHost}.";
            }
            catch (Exception ex)
            {
                ShowAndLogError(ex);
                System.Diagnostics.Debugger.Break();
            }
            IsInProgress = false;
        }

        private async Task GetMailFolderNamesAsync()
        {
            var mailFolderNames = await _imapReceiver.GetMailFolderNamesAsync();
            if (mailFolderNames.Count > 0)
            {
                ViewModelItems = new ObservableCollection<string>(mailFolderNames);
                StoreFolderNames(mailFolderNames);
            }
        }

        private void StoreFolderNames(IEnumerable<string> folderNames)
        {
            try
            {
                Guard.IsNotNull(folderNames, nameof(folderNames));
                //_dbContext?.Folders.UpdateRange(folderNames);
            }
            catch (Exception ex)
            {
                ShowAndLogWarning(ex);
            }
        }

        private bool isReceiving = false;

        [RelayCommand]
        private async Task ReceiveAsync()
        {
            if (isReceiving)
            {
                isReceiving = false;
                _cts.Cancel();
                _cts.TryReset();
                return;
            }
            try
            {
                await GetMailFolderNamesAsync().ConfigureAwait(false);
                //int progressPercentage = Convert.ToInt32((max * 100d) / 100);
                isReceiving = true;
                var tasks = new Task[]
                {
                    _imapReceiver.MonitorFolder.OnMessageArrival(EnqueueAsync).IdleAsync(_cts.Token),
                    ProcessQueueAsync(OnArrivalAsync, _cts.Token)
                };
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                isReceiving = false;
                ShowAndLogError(ex);
                //System.Diagnostics.Debugger.Break();
            }
        }

        private async Task EnqueueAsync(IMessageSummary m) => await _queue.Writer.WriteAsync(m, _cts.Token);

        private async Task ProcessQueueAsync(Func<IMessageSummary, ValueTask> messageArrivalMethod, CancellationToken cancellationToken = default)
        {
            IMessageSummary? messageItem = null;
            try
            {
                await foreach (var messageSummary in _queue.Reader.ReadAllAsync(cancellationToken))
                {
                    if (messageSummary != null)
                    {
                        messageItem = messageSummary;
                        await messageArrivalMethod(messageSummary);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogTrace("Arrival queue cancelled.");
            }
            catch (ChannelClosedException ex)
            {
                _logger.LogWarning(ex, "Channel closed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred processing task queue item #{0}.", messageItem);
                if (messageItem != null)
                    await _queue.Writer.WriteAsync(messageItem);
            }
        }

        private async ValueTask OnArrivalAsync(IMessageSummary messageSummary)
        {
            UpdateStatusText("Downloading email...");
            IsInProgress = true;
            var mimeMessage = await messageSummary.GetMimeMessageAsync(_cts.Token);
            var email = mimeMessage.Convert();
            UpdateStatusText($"{_imapReceiver} #{messageSummary.Index} received: {email.Subject}.");
            ViewModelDataGrid.Add(email);
            if (SelectedEmail == null)
            {
                SelectedEmail = email;
            }
            IsInProgress = false;
            //UpdateStatusText(string.Empty);
        }

        public void Dispose()
        {
            //_queue.Writer.Complete();
            _imapReceiver.Dispose();
            //_dbContext?.Dispose();
            _worker.Dispose();
            _cts.Dispose();
        }
    }
}
