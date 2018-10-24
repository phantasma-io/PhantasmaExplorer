﻿using System;
using System.Collections.Generic;
using System.IO;
using Phantasma.Blockchain;
using Phantasma.Core.Types;
using Phantasma.Cryptography;
using Phantasma.Explorer.Infrastructure.Data;
using Phantasma.Explorer.Site;
using Phantasma.VM.Utils;

namespace Phantasma.Explorer
{
    public class Explorer
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Initializing Phantasma Block Explorer....");
            var nexus = InitMockData();

            var curPath = Directory.GetCurrentDirectory();
            Console.WriteLine("Current path: " + curPath);

            var site = HostBuilder.CreateSite(args, "public");
            var viewsRenderer = new ViewsRenderer(site, "views");

            var mockRepo = new MockRepository { NexusChain = nexus };

            viewsRenderer.SetupControllers(mockRepo);
            viewsRenderer.Init();
            viewsRenderer.SetupHandlers();
            site.server.Run();
        }

        private static Nexus InitMockData()
        {
            var ownerKey = KeyPair.FromWIF("L2G1vuxtVRPvC6uZ1ZL8i7Dbqxk9VPXZMGvZu9C3LXpxKK51x41N");
            var simulator = new ChainSimulator(ownerKey, 12345);

            // generate blocks with mock transactions
            for (int i=1; i<=500; i++)
            {
                simulator.GenerateBlock();
            }

            var nexus = simulator.Nexus;

            var bankChain = nexus.FindChainByName("bank");

            var targetAddress = Address.FromText("PGasVpbFYdu7qERihCsR22nTDQp1JwVAjfuJ38T8NtrCB");

            // mainchain transfer
            {
                var transactions = new List<Transaction>();
                var script = ScriptUtils.CallContractScript(nexus.RootChain, "TransferTokens", ownerKey.Address, targetAddress, Nexus.NativeTokenSymbol, TokenUtils.ToBigInteger(5));
                var tx = new Transaction(script, 0, 0);
                tx.Sign(ownerKey);
                transactions.Add(tx);

                var block = new Block(nexus.RootChain, ownerKey.Address, Timestamp.Now, transactions, nexus.RootChain.LastBlock);
                if (!block.Chain.AddBlock(block))
                {
                    throw new Exception("test block failed");
                }
            }

            // side chain send
            Hash sideSendHash;
            {
                var transactions = new List<Transaction>();
                var script = ScriptUtils.CallContractScript(nexus.RootChain, "SendTokens", bankChain.Address, ownerKey.Address, targetAddress, Nexus.NativeTokenSymbol, TokenUtils.ToBigInteger(7));
                var tx = new Transaction(script, 0, 0);
                tx.Sign(ownerKey);
                transactions.Add(tx);

                var block = new Block(nexus.RootChain, ownerKey.Address, Timestamp.Now, transactions, nexus.RootChain.LastBlock);
                if (!block.Chain.AddBlock(block))
                {
                    throw new Exception("test block failed");
                }

                sideSendHash = tx.Hash;
            }

            // side chain receive
            {
                var transactions = new List<Transaction>();
                var script = ScriptUtils.CallContractScript(bankChain, "ReceiveTokens", nexus.RootChain.Address, targetAddress, sideSendHash);
                var tx = new Transaction(script, 0, 0);
                tx.Sign(ownerKey);
                transactions.Add(tx);

                var block = new Block(bankChain, ownerKey.Address, Timestamp.Now, transactions, nexus.RootChain.LastBlock);
                if (!block.Chain.AddBlock(block))
                {
                    throw new Exception("test block failed");
                }
            }
            return nexus;
        }
    }
}
