﻿// Copyright (C) 2017-2020 Ixian OU
// This file is part of Ixian DLT - www.github.com/ProjectIxian/Ixian-DLT
//
// Ixian DLT is free software: you can redistribute it and/or modify
// it under the terms of the MIT License as published
// by the Open Source Initiative.
//
// Ixian DLT is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// MIT License for more details.

using DLT;
using DLT.Meta;
using IXICore;
using IXICore.Meta;
using IXICore.Network;
using IXICore.Utils;
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;

namespace DLTNode
{
    class StressTest
    {
        public static TcpClient tcpClient = null;

        static string txfilename = "txspam.file";
        static string hostname = "192.168.1.101";
        static int port = 10515;
        static long targetTps = 100;
        static bool testInProgress = false;

        public static void start(string type, int tx_count, string to, int threads = 1)
        {
            new Thread(() =>
            {
                if (testInProgress == true)
                {
                    targetTps = tx_count;
                    return;
                }
                testInProgress = true;

                // Run protocol spam
                if (type.Equals("protocol", StringComparison.Ordinal))
                    startProtocolTest();

                // Run the spam connect test
                if (type.Equals("connect", StringComparison.Ordinal))
                    startSpamConnectTest();

                // Run transaction spam test
                if (type.Equals("txspam", StringComparison.Ordinal))
                {
                    if(to == null)
                    {
                        Logging.error("Cannot start txspam test, to parameters wasn't provided.");
                        return;
                    }
                    startTxSpamTest(tx_count, new Address(Base58Check.Base58CheckEncoding.DecodePlain(to)), threads);
                }

                // Run the transaction spam file generation test
                if (type.Equals("txfilegen", StringComparison.Ordinal))
                    startTxFileGenTest(tx_count);

                // Run the transaction spam file test
                if (type.Equals("txfilespam", StringComparison.Ordinal))
                {
                    targetTps = tx_count;
                    startTxFileSpamTest();
                }

                testInProgress = false;
            }).Start();
        }

        private static bool connect()
        {
            tcpClient = new TcpClient();

            // Don't allow another socket to bind to this port.
            tcpClient.Client.ExclusiveAddressUse = true;

            // The socket will linger for 3 seconds after 
            // Socket.Close is called.
            tcpClient.Client.LingerState = new LingerOption(true, 3);

            // Disable the Nagle Algorithm for this tcp socket.
            tcpClient.Client.NoDelay = true;

            tcpClient.Client.ReceiveTimeout = 5000;
            //tcpClient.Client.ReceiveBufferSize = 1024 * 64;
            //tcpClient.Client.SendBufferSize = 1024 * 64;
            tcpClient.Client.SendTimeout = 5000;

            try
            {
                tcpClient.Connect(hostname, port);
            }
            catch (SocketException se)
            {
                SocketError errorCode = (SocketError)se.ErrorCode;

                switch (errorCode)
                {
                    case SocketError.IsConnected:
                        break;

                    case SocketError.AddressAlreadyInUse:
                        Logging.warn(string.Format("Address already in use."));
                        break;

                    default:
                        {
                            Logging.warn(string.Format("Socket connection has failed."));
                        }
                        break;
                }

                try
                {
                    tcpClient.Client.Close();
                }
                catch (Exception)
                {
                    Logging.warn(string.Format("Socket exception when closing."));
                }

                disconnect();
                return false;
            }
            return true;
        }

        public static void disconnect()
        {
            if (tcpClient == null)
                return;

            if (tcpClient.Client.Connected)
            {
                tcpClient.Client.Shutdown(SocketShutdown.Both);
                // tcpClient.Client.Disconnect(true);
                tcpClient.Close();
            }
        }


        public static void startSpamConnectTest()
        {
            Logging.info("Starting spam connect test");

            for (int i = 0; i < 100; i++)
            {
                if (connect())
                    Logging.info("Connected.");

            //    disconnect();
            }

            Logging.info("Ending spam connect test");
        }
     
        public static void startProtocolTest()
        {
            Logging.info("Starting spam connect test");

            if(connect())
            {
                Logging.info("Connected.");
            }

            WalletStorage ws = IxianHandler.getWalletStorage();

            using (MemoryStream m = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(m))
                {

                    string publicHostname = IxianHandler.getFullPublicAddress();
                    // Send the public IP address and port
                    writer.Write(publicHostname);

                    // Send the public node address
                    Address address = ws.getPrimaryAddress();
                    writer.Write(address.addressNoChecksum);

                    // Send the testnet designator
                    writer.Write(IxianHandler.isTestNet);

                    // Send the node type
                    char node_type = 'M'; // This is a Master node
                    writer.Write(node_type);

                    // Send the node device id
                    writer.Write(CoreConfig.device_id);

                    // Send the wallet public key
                    writer.Write(ws.getPrimaryPublicKey());

                    sendData(ProtocolMessageCode.hello, m.ToArray());
                }
            }

            Transaction tx = new Transaction((int)Transaction.Type.PoWSolution, "0", "0", ConsensusConfig.foundationAddress, ws.getPrimaryAddress(), null, null, Node.blockChain.getLastBlockNum());

            //byte[] data = string.Format("{0}||{1}||{2}", Node.walletStorage.publicKey, 0, 1);
            //tx.data = data;

            sendData(ProtocolMessageCode.transactionData2, tx.getBytes(true, true));


            disconnect();
            

            Logging.info("Ending spam connect test");
        }

        public static void startTxSpamTest(int tx_count, Address to, int threads)
        {
            Logging.info("Starting tx spam test");

            IxiNumber amount = ConsensusConfig.transactionPrice;
            IxiNumber fee = ConsensusConfig.transactionPrice;
            WalletStorage ws = IxianHandler.getWalletStorage();
            Address from = ws.getPrimaryAddress();
            Address pubKey = new Address(ws.getPrimaryPublicKey());

            for (int thread = 0; thread < threads; thread++)
            {
                new Thread(() =>
                {
                    long start_time = Clock.getTimestampMillis();
                    int spam_counter = 0;
                    for (int i = 0; i < tx_count; i++)
                    {
                        if (pubKey != null)
                        {
                            // Check if this wallet's public key is already in the WalletState
                            Wallet mywallet = Node.walletState.getWallet(from);
                            if (mywallet.publicKey != null && mywallet.publicKey.SequenceEqual(pubKey.pubKey))
                            {
                                // Walletstate public key matches, we don't need to send the public key in the transaction
                                pubKey = null;
                            }
                        }

                        Transaction transaction = new Transaction((int)Transaction.Type.Normal, amount, fee, to, from, null, pubKey, Node.blockChain.getLastBlockNum());
                        // Console.WriteLine("> sending {0}", transaction.id);
                        TransactionPool.addTransaction(transaction);

                        spam_counter++;
                        if (spam_counter >= targetTps)
                        {
                            long elapsed = Clock.getTimestampMillis() - start_time;
                            if (elapsed < 1000)
                            {
                                Thread.Sleep(1000 - (int)elapsed);
                            }
                            spam_counter = 0;
                            start_time = Clock.getTimestampMillis();
                        }
                    }
                }).Start();
            }

            Logging.info("Ending tx spam test");
        }

        public static void startTxFileGenTest(int tx_count)
        {
            Logging.info("Starting tx file gen test");

            BinaryWriter writer;
            try
            {
                writer = new BinaryWriter(new FileStream(txfilename, FileMode.Create));
            }
            catch (IOException e)
            {
                Logging.error(String.Format("Cannot create txspam file. {0}", e.Message));
                return;
            }

            int nonce = 0; // Set the starting nonce

            WalletStorage ws = IxianHandler.getWalletStorage();

            writer.Write(tx_count);
            for (int i = 0; i < tx_count; i++)
            {
                IxiNumber amount = new IxiNumber("0.01");
                IxiNumber fee = ConsensusConfig.transactionPrice;
                Address to = ConsensusConfig.foundationAddress;
                Address from = ws.getPrimaryAddress();

                Address pubKey = new Address(ws.getPrimaryPublicKey());
                // Check if this wallet's public key is already in the WalletState
                Wallet mywallet = Node.walletState.getWallet(from);
                if (mywallet.publicKey != null && mywallet.publicKey.SequenceEqual(pubKey.pubKey))
                {
                    // Walletstate public key matches, we don't need to send the public key in the transaction
                    pubKey = null;
                }

                Transaction transaction = new Transaction((int)Transaction.Type.Normal, amount, fee, to, from, null, pubKey, Node.blockChain.getLastBlockNum());
                byte[] bytes = transaction.getBytes(true, true);
                
                writer.Write(bytes.Length);
                writer.Write(bytes);

                nonce++;
            }

            writer.Close();

            Logging.info("Ending tx file gen test");
        }

        public static void startTxFileSpamTest()
        {
            Logging.info("Starting tx file spam test");

            if (File.Exists(txfilename) == false)
            {
                Logging.error("Cannot start tx file spam test. Missing tx spam file!");
                return;
            }

            BinaryReader reader;
            try
            {
                reader = new BinaryReader(new FileStream(txfilename, FileMode.Open));
            }
            catch (IOException e)
            {
                Logging.error("Cannot open txspam file. {0}", e.Message);
                return;
            }

            try
            {
                int spam_num = reader.ReadInt32();
                Logging.info("Reading {0} spam transactions from file.", spam_num);
                long start_time = Clock.getTimestampMillis();
                int spam_counter = 0;
                for (int i = 0; i < spam_num; i++)
                {
                    int length = reader.ReadInt32();
                    byte[] bytes = reader.ReadBytes(length);
                    Transaction transaction = new Transaction(bytes, false, true);
                    TransactionPool.addTransaction(transaction);
                    spam_counter++;
                    if(spam_counter >= targetTps)
                    {
                        long elapsed = Clock.getTimestampMillis() - start_time;
                        if (elapsed < 1000)
                        {
                            Thread.Sleep(1000 - (int)elapsed);
                        }
                        spam_counter = 0;
                        start_time = Clock.getTimestampMillis();
                    }
                }
            }
            catch (IOException e)
            {
                Logging.error("Cannot read from txspam file. {0}", e.Message);
                return;
            }
            reader.Close();

            Logging.info("Ending tx file spam test");
        }



        // Sends data over the network
        public static void sendData(ProtocolMessageCode code, byte[] data)
        {
            byte[] ba = RemoteEndpoint.prepareProtocolMessage(code, data, CoreConfig.protocolVersion, 0);
            NetDump.Instance.appendSent(tcpClient.Client, ba, ba.Length);
            try
            {
                tcpClient.Client.Send(ba, SocketFlags.None);
                if (tcpClient.Client.Connected == false)
                {
                    Logging.error("Failed senddata to client. Reconnecting.");

                }
            }
            catch (Exception e)
            {
                Logging.error(String.Format("CLN: Socket exception, attempting to reconnect {0}", e));
            }
            //Console.WriteLine("sendData done");
        }
    }
}
