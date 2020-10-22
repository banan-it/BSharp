﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Twilio;
using Twilio.Exceptions;
using Twilio.Rest.Api.V2010.Account;

namespace Tellma.Services.Sms
{
    public class TwilioSmsSender : ISmsSender
    {
        private readonly SmsOptions _options;
        private readonly ILogger _logger;
        private readonly Random _rand = new Random();

        public TwilioSmsSender(IOptions<SmsOptions> options, ILogger<TwilioSmsSender> logger)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;
        }

        public async Task<string> SendAsync(string toPhoneNumber, string sms, CancellationToken cancellation)
        {
            var serviceSid = !string.IsNullOrWhiteSpace(_options.ServiceSid) ? _options.ServiceSid : throw new InvalidOperationException("ServiceSid is missing");
            var to = new Twilio.Types.PhoneNumber(toPhoneNumber);

            // Exponential backoff
            const int maxAttempts = 5;
            const int maxBackoff = 25000; // 25 Seconds
            const int baseBackoff = 1000; // 1 Second
            int attemptsSoFar = 0;
            int backoff = baseBackoff;
            
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    attemptsSoFar++;

                    // Send using Twilio's Messaging Service
                    var message = await MessageResource.CreateAsync(
                        body: sms,
                        messagingServiceSid: serviceSid,
                        to: to
                    );

                    return message.Sid; // Breaks the while (true) loop
                }
                catch (ApiException ex) when (ex.Status == (int)HttpStatusCode.TooManyRequests || ex.Status >= 500)
                {
                    // Twilio imposes a maximum number of concurrent calls, and returns 429 when that maximum is reached
                    // Here we implement exponential backoff attempts to retry the call few more times before giving up
                    // As recommended here https://bit.ly/2CWYrjQ
                    if (attemptsSoFar < maxAttempts)
                    {
                        var randomOffset = _rand.Next(0, 1000);
                        await Task.Delay(backoff + randomOffset, cancellation);

                        // Double the backoff for next attempt
                        backoff = Math.Min(backoff * 2, maxBackoff);
                    }
                    else
                    {
                        _logger.LogError($"Twilio: 429 Too Many Requests even after {attemptsSoFar} attempts with exponential backoff.");

                        throw; // Give up
                    }
                }
            }

            // The request was cancelled, it doesn't matter what we return
            return "";
        }

        #region Bulk SMS

        //public async Task<string> BulkSendAsync(IEnumerable<string> phoneNumbers, string sms, CancellationToken _)
        //{
        //    var serviceSid = !string.IsNullOrWhiteSpace(_smsOpt.ServiceSid) ? _smsOpt.ServiceSid : throw new InvalidOperationException("ServiceSid is missing");
        //    var binding = phoneNumbers.Select(to => $"{{\"binding_type\":\"sms\",\"address\":\"{to}\"}}").ToList();

        //    // Send the SMS through the Twilio API
        //    var notification = await Twilio.Rest.Notify.V1.Service.NotificationResource.CreateAsync(serviceSid, toBinding: binding, body: sms);

        //    return notification.Sid;
        //}

        #endregion
    }
}