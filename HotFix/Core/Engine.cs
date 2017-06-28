using System;
using System.Runtime.CompilerServices;
using HotFix.Transport;
using HotFix.Utilities;

namespace HotFix.Core
{
    public class Engine
    {
        public static IClock Clock { get; set; }
        public Func<IConfiguration, ITransport> Transports { get; set; }

        public Engine()
        {
            Clock = new RealTimeClock();
            Transports = c => new TcpTransport(c.Host, c.Port);
        }

        public void Run(IConfiguration configuration)
        {
            var transport = Transports(configuration);
            var channel = new Channel(transport);

            var inbound = new FIXMessage();
            var outbound = new FIXMessageWriter(1024, configuration.Version);

            configuration.InboundTimestamp = Clock.Time;
            configuration.OutboundTimestamp = Clock.Time;

            HandleLogon(configuration, channel, inbound, outbound);

            while (true)
            {
                if (inbound.Valid)
                {
                    if (!inbound[8].Is(configuration.Version)) throw new EngineException("Unexpected begin string received");
                    if (!inbound[49].Is(configuration.Target)) throw new EngineException("Unexpected comp id received");
                    if (!inbound[56].Is(configuration.Sender)) throw new EngineException("Unexpected comp id received");

                    if (inbound[34].Is(configuration.InboundSeqNum))
                    {
                        configuration.Synchronizing = false;
                        configuration.TestRequestPending = false;

                        // Process message
                        Console.WriteLine("Processing: " + inbound[35].AsString);

                        switch (inbound[35].AsString)
                        {
                            case "1":
                                HandleTestRequest(configuration, channel, inbound, outbound);
                                break;
                            case "2":
                                HandleResendRequest(configuration, channel, inbound, outbound);
                                break;
                            case "4":
                                HandleSequenceReset(configuration, channel, inbound, outbound);
                                break;
                            default:
                                break;
                        }

                        configuration.InboundSeqNum++;
                        configuration.InboundTimestamp = Clock.Time;
                    }
                    else
                    {
                        if (inbound[34].AsLong < configuration.InboundSeqNum) throw new EngineException("Sequence number too low");
                        if (inbound[34].AsLong > configuration.InboundSeqNum) SendResendRequest(configuration, channel, outbound);
                    }
                }

                if (Clock.Time - configuration.OutboundTimestamp > TimeSpan.FromSeconds(configuration.HeartbeatInterval))
                {
                    SendHeartbeat(configuration, channel, outbound);
                }

                if (Clock.Time - configuration.InboundTimestamp > TimeSpan.FromSeconds(configuration.HeartbeatInterval * 1.2))
                {
                    if (Clock.Time - configuration.InboundTimestamp > TimeSpan.FromSeconds(configuration.HeartbeatInterval * 2))
                    {
                        throw new EngineException("Did not receive any messages for too long");
                    }

                    if (!configuration.TestRequestPending)
                    {
                        SendTestRequest(configuration, channel, outbound);
                        configuration.TestRequestPending = true;
                    }
                }

                inbound.Clear();

                channel.Read(inbound);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendHeartbeat(IConfiguration configuration, Channel channel, FIXMessageWriter outbound)
        {
            outbound.Prepare("0");
            outbound.Set(34, configuration.OutboundSeqNum);
            outbound.Set(52, Clock.Time);
            outbound.Set(49, configuration.Sender);
            outbound.Set(56, configuration.Target);
            outbound.Build();

            Send(configuration, channel, outbound);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendTestRequest(IConfiguration configuration, Channel channel, FIXMessageWriter outbound)
        {
            outbound.Prepare("1");
            outbound.Set(34, configuration.OutboundSeqNum);
            outbound.Set(52, Clock.Time);
            outbound.Set(49, configuration.Sender);
            outbound.Set(56, configuration.Target);
            outbound.Set(112, Clock.Time.Ticks);
            outbound.Build();

            Send(configuration, channel, outbound);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendResendRequest(IConfiguration configuration, Channel channel, FIXMessageWriter outbound)
        {
            if (configuration.Synchronizing) return;

            outbound.Prepare("2");
            outbound.Set(34, configuration.OutboundSeqNum);
            outbound.Set(52, Clock.Time);
            outbound.Set(49, configuration.Sender);
            outbound.Set(56, configuration.Target);
            outbound.Set(7, configuration.InboundSeqNum);
            outbound.Set(16, 0);
            outbound.Build();

            Send(configuration, channel, outbound);

            configuration.Synchronizing = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HandleTestRequest(IConfiguration configuration, Channel channel, FIXMessage inbound, FIXMessageWriter outbound)
        {
            // Prepare and send a heartbeat (with the test request id)
            outbound.Prepare("0");
            outbound.Set(34, configuration.OutboundSeqNum);
            outbound.Set(52, Clock.Time);
            outbound.Set(49, configuration.Sender);
            outbound.Set(56, configuration.Target);
            outbound.Set(112, inbound[112].AsString);
            outbound.Build();

            Send(configuration, channel, outbound);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HandleResendRequest(IConfiguration configuration, Channel channel, FIXMessage inbound, FIXMessageWriter outbound)
        {
            // Validate request
            if (!inbound[16].Is(0L)) throw new EngineException("Unsupported resend request received (partial gap fills are not supported)");

            // Prepare and send a gap fill message
            outbound.Prepare("4");
            outbound.Set(34, inbound[7].AsLong);
            outbound.Set(52, Clock.Time);
            outbound.Set(49, configuration.Sender);
            outbound.Set(56, configuration.Target);
            outbound.Set(123, "Y");
            outbound.Set(36, configuration.OutboundSeqNum);
            outbound.Build();

            Send(configuration, channel, outbound);

            // HACK
            configuration.OutboundSeqNum--;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HandleSequenceReset(IConfiguration configuration, Channel channel, FIXMessage inbound, FIXMessageWriter outbound)
        {
            // Validate request
            if (!inbound.Contains(123) || !inbound[123].Is("Y")) throw new Exception("Unsupported sequence reset received (hard reset)");
            if (inbound[36].AsLong <= configuration.InboundSeqNum) throw new Exception("Invalid sequence reset received (bad new seq num)");

            // Accept the new sequence number
            configuration.InboundSeqNum = inbound[36].AsLong;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Send(IConfiguration configuration, Channel channel, FIXMessageWriter message)
        {
            channel.Write(message);

            configuration.OutboundSeqNum++;
            configuration.OutboundTimestamp = Clock.Time;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HandleLogon(IConfiguration configuration, Channel channel, FIXMessage inbound, FIXMessageWriter outbound)
        {
            outbound.Prepare("A");
            outbound.Set(34, configuration.OutboundSeqNum);
            outbound.Set(52, Clock.Time);
            outbound.Set(49, configuration.Sender);
            outbound.Set(56, configuration.Target);
            outbound.Set(108, configuration.HeartbeatInterval);
            outbound.Set(98, 0);
            outbound.Set(141, "Y");
            outbound.Build();

            Send(configuration, channel, outbound);

            while (Clock.Time - configuration.OutboundTimestamp < TimeSpan.FromSeconds(10))
            {
                channel.Read(inbound);

                if (inbound.Valid)
                {
                    if (!inbound[35].Is("A")) throw new EngineException("Unexpected first message received (expected a logon)");
                    if (!inbound[108].Is(configuration.HeartbeatInterval)) throw new EngineException("Unexpected heartbeat interval received");
                    if (!inbound[ 98].Is(0)) throw new EngineException("Unexpected encryption method received");
                    if (!inbound[141].Is("Y")) throw new EngineException("Unexpected reset on logon received");

                    return;
                }
            }

            throw new EngineException("Logon response not received on time");
        }
    }

    public class EngineException : Exception
    {
        public EngineException()
        {
            
        }

        public EngineException(string message) : base(message)
        {
            
        }
    }
}