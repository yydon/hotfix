﻿using System;

namespace HotFix.Core
{
    public interface IConfiguration
    {
        Role Role { get; set; }

        string Version { get; set; }
        string Target { get; set; }
        string Sender { get; set; }

        string Host { get; set; }
        int Port { get; set; }

        int HeartbeatInterval { get; set; }
        long InboundSeqNum { get; set; }
        long OutboundSeqNum { get; set; }
    }
}