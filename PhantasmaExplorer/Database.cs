﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LunarLabs.Parser;
using Phantasma.Core.Types;
using Phantasma.Cryptography;
using Phantasma.Domain;
using Phantasma.Numerics;
using Phantasma.VM;

namespace Phantasma.Explorer
{
    public abstract class ExplorerObject
    {
        public readonly NexusData Nexus;

        public ExplorerObject(NexusData database)
        {
            this.Nexus = database;
        }
    }

    public class BlockData : ExplorerObject, IBlock
    {
        public Address ChainAddress { get; private set; }
        public BigInteger Height { get; private set; }
        public Timestamp Timestamp { get; private set; }
        public Hash PreviousHash { get; private set; }
        public uint Protocol { get; private set; }
        public Hash Hash { get; private set; }
        public Hash[] TransactionHashes { get; private set; }
        public IOracleEntry[] OracleData { get; private set; }

        public Address ValidatorAddress { get; private set; }

        public TransactionData[] Transactions { get; private set; }

        public ChainData Chain { get; private set; }

        public DateTime Date => (DateTime)Timestamp;

        public BlockData(NexusData database, DataNode node) : base(database)
        {
            ChainAddress = Address.FromText(node.GetString("chainAddress"));
            Chain = Nexus.FindChainByAddress(this.ChainAddress);

            Height = BigInteger.Parse(node.GetString("height"));
            Timestamp = new Timestamp(node.GetUInt32("timestamp"));
            PreviousHash = Hash.Parse(node.GetString("previousHash"));
            Protocol = node.GetUInt32("protocol");
            Hash = Hash.Parse(node.GetString("hash"));

            ValidatorAddress = Address.FromText(node.GetString("validatorAddress"));

            OracleData = null; // TODO

            var txsNode = node.GetNode("txs");
            TransactionHashes = new Hash[txsNode.ChildCount];
            Transactions = new TransactionData[TransactionHashes.Length];

            Nexus.DoParallelRequests($"Fetching transactions for block {Hash}...", Transactions.Length, (index) =>
            {
                var temp = txsNode.GetNodeByIndex(index);
                var txHash = Hash.Parse(temp.GetString("hash"));
                TransactionHashes[index] = txHash;
                var tx = Nexus.FindTransaction(Chain, txHash);
                Transactions[index] = tx;
                tx.Block = this;
            });
        }
    }

    public class TransactionData : ExplorerObject, ITransaction
    {
        public TransactionData(NexusData database, DataNode node) : base(database)
        {
            Script = Base16.Decode(node.GetString("script"));
            NexusName = null; // TODO
            var addr = Address.FromText(node.GetString("chainAddress"));
            Chain = database.FindChainByAddress(addr);
            ChainName = Chain.Name;
            Expiration = 0; // TODO
            Payload = null; // TODO
            Signatures = null; // TODO
            Hash = Hash.Parse(node.GetString("hash"));

            Timestamp = new Timestamp(node.GetUInt32("timestamp"));

            Result = node.GetString("result");
            if (string.IsNullOrEmpty(Result))
            {
                Result = "-";
            }

            var eventsNode = node.GetNode("events");
            Events = new Event[eventsNode.ChildCount];
            for (int i=0; i<Events.Length; i++)
            {
                var temp = eventsNode.GetNodeByIndex(i);

                var kind = temp.GetEnum<EventKind>("kind");
                var contract = temp.GetString("contract");
                addr = Address.FromText( temp.GetString("address"));
                var data = Base16.Decode(temp.GetString("data"));

                Events[i] = new Event(kind, addr, contract, data);
            }

            var disasm = new Disassembler(this.Script);
            this.Instructions = disasm.Instructions.ToArray();
        }

        public ChainData Chain { get; private set; }

        public BlockData Block { get; internal set; }

        public byte[] Script { get; private set; }
        public string NexusName { get; private set; }
        public string ChainName { get; private set; }
        public Timestamp Expiration { get; private set; }
        public byte[] Payload { get; private set; }
        public Signature[] Signatures { get; private set; }
        public Hash Hash { get; private set; }
        public Event[] Events { get; private set; }

        public string Result { get; private set; }

        public Timestamp Timestamp { get; private set; }
        public DateTime Date => (DateTime)Timestamp;

        public Instruction[] Instructions { get; set; }

        private string _description = null;
        public string Description {
            get
            {
                if (_description == null)
                {
                    _description = GenerateDescription();
                }

                return _description;
            }
        }

        private string LinkAddress(Address address, string name = null)
        {
            return LinkAddressTag.GenerateLink(this.Nexus, address, name);
        }

        private string LinkToken(string symbol)
        {
            var token = Nexus.FindTokenBySymbol(symbol);

            var name = token != null ? token.Name : symbol;

            return $"<a href=\"/token/{symbol}\">{symbol}</a>";
        }

        private string GenerateDescription()
        {
            decimal totalFees = 0;
            var fees = new Dictionary<Address, decimal>();

            Address feeAddress = Address.Null;

            var sb = new StringBuilder();
            foreach (var evt in Events)
            {
                if (evt.Contract == "gas")
                {
                    switch (evt.Kind)
                    {
                        case EventKind.TokenStake:
                            {
                                var data = evt.GetContent<TokenEventData>();
                                if (data.Symbol == DomainSettings.FuelTokenSymbol)
                                {
                                    feeAddress = evt.Address;
                                }
                                break;
                            }

                        case EventKind.TokenClaim:
                            {
                                var data = evt.GetContent<TokenEventData>();
                                if (data.Symbol == DomainSettings.FuelTokenSymbol)
                                {
                                    if (evt.Address.IsSystem)
                                    {
                                        var amount = UnitConversion.ToDecimal(data.Value, DomainSettings.FuelTokenDecimals);
                                        totalFees += amount;

                                        decimal fee = fees.ContainsKey(evt.Address) ? fees[evt.Address] : 0;
                                        fee += amount;

                                        fees[evt.Address] = fee;
                                    }
                                }
                                break;
                            }
                    }

                    continue;
                }

                switch (evt.Kind)
                {
                    case EventKind.BlockCreate:
                        sb.AppendLine("Started Block");
                        break;

                    case EventKind.BlockClose:
                        sb.AppendLine( "Closed Block");
                        break;

                    case EventKind.ChainCreate:
                        {
                            var name = evt.GetContent<string>();
                            sb.AppendLine($"Created chain: <a href=\"/chain/{name}\">{name}</a>");
                            break;
                        }

                    case EventKind.TokenCreate: 
                        {
                            var symbol = evt.GetContent<string>();
                            sb.AppendLine($"Created token: {LinkToken(symbol)}");
                            break;
                        }

                    case EventKind.AddressRegister:
                        {
                            var name = evt.GetContent<string>();
                            sb.AppendLine($"Registered name: {name} for {LinkAddress(evt.Address, name)}");
                            break;
                        }

                    case EventKind.ContractDeploy:
                        {
                            var name = evt.GetContent<string>();
                            sb.AppendLine($"Deployed contract: {name}</a>");
                            break;
                        }

                    case EventKind.PlatformCreate:
                        {
                            var name = evt.GetContent<string>();
                            sb.AppendLine($"Created platform: {name}");
                            break;
                        }

                    case EventKind.TransactionSettle:
                        {
                            var data = evt.GetContent<TransactionSettleEventData>();
                            sb.AppendLine($"Settled {data.Platform} transaction <a href=\"https://neoscan.io/transaction/{data.Hash}\">{data.Hash}</a>");
                            break;
                        }

                    case EventKind.ValidatorElect:
                        {
                            var address = evt.GetContent<Address>();
                            sb.AppendLine($"Elected validator: {LinkAddress(address)}");
                            break;
                        }

                    case EventKind.ValidatorPropose:
                        {
                            var address = evt.GetContent<Address>();
                            sb.AppendLine($"Proposed validator: {LinkAddress(address)}");
                            break;
                        }

                    case EventKind.ValidatorSwitch:
                        {
                            sb.AppendLine($"Switched validator: {LinkAddress(evt.Address)}");
                            break;
                        }

                    case EventKind.ValueCreate:
                        {
                            var data = evt.GetContent<ChainValueEventData>();
                            sb.AppendLine($"Created governance value: {data.Name}");
                            break;
                        }

                    case EventKind.ValueUpdate:
                        {
                            var data = evt.GetContent<ChainValueEventData>();
                            sb.AppendLine($"Updated governance value: {data.Name}");
                            break;
                        }

                    case EventKind.GasEscrow:
                        {
                            var data = evt.GetContent<GasEventData>();
                            sb.AppendLine($"{LinkAddress(evt.Address)} escrowed {data.amount} fuel at price {UnitConversion.ToDecimal(data.price, DomainSettings.FuelTokenDecimals)} {DomainSettings.FuelTokenSymbol}");
                            break;
                        }

                    case EventKind.GasPayment:
                        {
                            var data = evt.GetContent<GasEventData>();
                            sb.AppendLine($"{LinkAddress(evt.Address)} paid {data.amount} fuel at price {UnitConversion.ToDecimal(data.price, DomainSettings.FuelTokenDecimals)} {DomainSettings.FuelTokenSymbol}");
                            break;
                        }

                    case EventKind.TokenMint:
                        {
                            var data = evt.GetContent<TokenEventData>();
                            var token = Nexus.FindTokenBySymbol(data.Symbol);
                            sb.AppendLine($"{LinkAddress(evt.Address)} minted {UnitConversion.ToDecimal(data.Value, token != null ? token.Decimals : 0)} {LinkToken(data.Symbol)}");
                            break;
                        }

                    case EventKind.TokenBurn:
                        {
                            var data = evt.GetContent<TokenEventData>();
                            var token = Nexus.FindTokenBySymbol(data.Symbol);
                            sb.AppendLine($"{LinkAddress(evt.Address)} burned {UnitConversion.ToDecimal(data.Value, token != null ? token.Decimals : 0)} {LinkToken(data.Symbol)}");
                            break;
                        }

                    case EventKind.TokenClaim:
                        {
                            var data = evt.GetContent<TokenEventData>();
                            var token = Nexus.FindTokenBySymbol(data.Symbol);
                            sb.AppendLine($"{LinkAddress(evt.Address)} claimed {UnitConversion.ToDecimal(data.Value, token != null ? token.Decimals : 0)} {LinkToken(data.Symbol)}");
                            break;
                        }

                    case EventKind.TokenStake:
                        {
                            var data = evt.GetContent<TokenEventData>();
                            var token = Nexus.FindTokenBySymbol(data.Symbol);

                            if (evt.Contract == "entry")
                            {
                                sb.AppendLine($"{LinkAddress(evt.Address)} staked {UnitConversion.ToDecimal(data.Value, token != null ? token.Decimals : 0)} {LinkToken(data.Symbol)}");
                            }
                            else
                            {
                                var contractAddress = Address.FromHash(evt.Contract);
                                sb.AppendLine($"{LinkAddress(evt.Address)} deposited {UnitConversion.ToDecimal(data.Value, token != null ? token.Decimals : 0)} {LinkToken(data.Symbol)} into {LinkAddress(contractAddress, evt.Contract)} contract");
                            }
                            break;
                        }

                    case EventKind.TokenSend:
                        {
                            var data = evt.GetContent<TokenEventData>();
                            var token = Nexus.FindTokenBySymbol(data.Symbol);
                            sb.AppendLine($"{LinkAddress(evt.Address)} sent {UnitConversion.ToDecimal(data.Value, token != null ? token.Decimals : 0)} {LinkToken(data.Symbol)}");
                            break;
                        }

                    case EventKind.TokenReceive:
                        {
                            var data = evt.GetContent<TokenEventData>();
                            var token = Nexus.FindTokenBySymbol(data.Symbol);
                            sb.AppendLine($"{LinkAddress(evt.Address)} received {UnitConversion.ToDecimal(data.Value, token != null ? token.Decimals : 0)} {LinkToken(data.Symbol)}");
                            break;
                        }
                }
            }

            if (!feeAddress.IsNull)
            {
                sb.AppendLine($"{LinkAddress(feeAddress)} paid {totalFees} {LinkToken(DomainSettings.FuelTokenSymbol)} in fees.");
            }

            foreach (var entry in fees)
            {
                sb.AppendLine($"{LinkAddress(entry.Key)} received {entry.Value} {LinkToken(DomainSettings.FuelTokenSymbol)} in fees.");
            }

            if (sb.Length > 0)
            {
                return sb.ToString().Replace("\n","<br>");
            }

            return "Custom Transaction";
        }
    }

    public class ChainData : ExplorerObject, IChain
    {
        public ChainData(NexusData Database, DataNode node) : base(Database)
        {
            this.Name = node.GetString("name");
            this.Address = Address.FromText(node.GetString("address"));
            this.Height = BigInteger.Parse(node.GetString("height"));
            this.BlockList = new List<BlockData>((int)Height);

            var contractsNodes = node.GetNode("contracts");
            if (contractsNodes != null)
            {
                Contracts = new Address[contractsNodes.ChildCount];
                for (int i=0; i<Contracts.Length; i++)
                {
                    var name = contractsNodes.GetString(i);
                    Contracts[i] = Address.FromHash(name);
                }
            }
            else
            {
                Contracts = new Address[0];
            }
        }

        public void Grow(int height)
        {
            if (height < 0)
            {
                throw new Exception("Invalid chain height: " + height);
            }

            this.Height = height;
            UpdateBlocks();
        }

        internal void UpdateBlocks()
        {
            int currentHeight = BlockList.Count;

            int newBlocks = 0;
            while (BlockList.Count < Height)
            {
                BlockList.Add(null);
                newBlocks++;
            }

            if (newBlocks <= 0)
            {
                return;
            }

            Nexus.DoParallelRequests($"Fetching new blocks for chain {Name}...", newBlocks, (index) =>
            {
                var ofs = index + currentHeight;
                BlockList[ofs] = Nexus.FindBlockByHeight(this, ofs + 1);
            });
        }

        public string Name { get; private set; }
        public Address Address { get; private set; }
        public BigInteger Height { get; private set; }

        public ChainData ParentChain { get; private set; }
        public ChainData[] ChildChains { get; private set; }

        public Address[] Contracts { get; private set; }

        public List<BlockData> BlockList { get; private set; }

        public BlockData LastBlock => BlockList[BlockList.Count - 1];

        public IEnumerable<BlockData> Blocks => BlockList.Skip(Math.Max(0, (int)(Height - 20))).Reverse();
    }

    public class ContractData : ExplorerObject, IContract
    {
        public string Name { get; private set; }

        public ContractInterface ABI => throw new NotImplementedException();

        public ContractData(NexusData database, DataNode node): base(database)
        {

        }
    }

    public class TokenData : ExplorerObject, IToken
    {
        public TokenData(NexusData database, DataNode node) : base(database)
        {
            this.Name = node.GetString("name");
            this.Symbol = node.GetString("symbol");
            this.Platform = node.GetString("platform");
            this.Hash = Hash.Parse(node.GetString("hash"));
            this.MaxSupply = BigInteger.Parse(node.GetString("maxSupply"));
            this.CurrentSupply = BigInteger.Parse(node.GetString("currentSupply"));
            this.Decimals = int.Parse(node.GetString("decimals"));
            this.Flags = node.GetEnum<TokenFlags>("flags");
            this.Script = null; // TODO
        }

        public string Name { get; private set; }
        public string Symbol { get; private set; }
        public string Platform { get; private set; }
        public Hash Hash { get; private set; }
        public TokenFlags Flags { get; private set; }
        public BigInteger MaxSupply { get; private set; }
        public BigInteger CurrentSupply { get; private set; }
        public int Decimals { get; private set; }
        public byte[] Script { get; private set; }

        public decimal FormattedMaxSupply => UnitConversion.ToDecimal(MaxSupply, Decimals);
        public decimal FormattedCurrentSupply => UnitConversion.ToDecimal(CurrentSupply, Decimals);

        public decimal Price = 0;

        public bool IsFungible => this.Flags.HasFlag(TokenFlags.Fungible);
        public bool IsFiat => this.Flags.HasFlag(TokenFlags.Fiat);
        public bool IsFinite => this.Flags.HasFlag(TokenFlags.Finite);
        public bool IsTransferable => this.Flags.HasFlag(TokenFlags.Transferable);
    }

    public class PlatformData : ExplorerObject, IPlatform
    {
        public PlatformData(NexusData database, DataNode node) :base(database)
        {
            Name = node.GetString("platform");
            Symbol = node.GetString("fuel");
            InteropAddresses = null;// platform.interop.Select(x => new PlatformSwapAddress() { ExternalAddress = x.external, LocalAddress = Address.FromText(x.local) }).ToArray();
        }

        public string Name { get; private set; }
        public string Symbol { get; private set; }
        public PlatformSwapAddress[] InteropAddresses { get; private set; }
    }

    public class BalanceData: ExplorerObject
    {
        public ChainData Chain { get; private set; }
        public BigInteger Amount { get; private set; }
        public string Symbol { get; private set; }
        public BigInteger[] IDs { get; private set; }

        private int decimals;

        public decimal FormattedAmount => UnitConversion.ToDecimal(Amount, decimals);

        public BalanceData(NexusData database, DataNode node) : base(database)
        {
            var chainName = node.GetString("chain");
            Chain = database.FindChainByName(chainName);
            Symbol = node.GetString("symbol");
            decimals = node.GetInt32("decimals");
            Amount = BigInteger.Parse(node.GetString("amount"));

            var idNode = node.GetNode("ids");
            if (idNode != null)
            {
                IDs = new BigInteger[idNode.ChildCount];
                for (int i = 0; i < IDs.Length; i++)
                {
                    IDs[i] = BigInteger.Parse(idNode.GetString(i));
                }
            }
            else
            {
                IDs = null;
            }
        }
    }

    public class AccountData : ExplorerObject
    {
        public AccountData(NexusData database, DataNode node) : base(database)
        {
            Name = node.GetString("name");
            Address = Cryptography.Address.FromText(node.GetString("address"));

            Stake = BigInteger.Parse( node.GetString("stake"));

            var balancesNode = node.GetNode("balances");
            if (balancesNode != null)
            {
                Balances = new BalanceData[balancesNode.ChildCount];
                for (int i = 0; i < Balances.Length; i++)
                {
                    var temp = balancesNode.GetNodeByIndex(i);
                    Balances[i] = new BalanceData(database, temp);
                }
            }
            else
            {
                Balances = new BalanceData[0];
            }

            LastTime = DateTime.UtcNow;
        }

        internal DateTime LastTime { get; private set; }

        public string Name { get; private set; }
        public Address Address { get; private set; }

        public BigInteger Stake { get; private set; }
        public decimal FormattedStake => UnitConversion.ToDecimal(Stake, DomainSettings.StakingTokenDecimals);

        public BalanceData[] Balances { get; private set; }

        public TransactionData[] Transactions { get; private set; }

        public bool IsEmpty
        {
            get
            {
                if (Address.Kind != AddressKind.User)
                {
                    return false;
                }

                return Name==ValidationUtils.ANONYMOUS && Stake == 0 && Balances.Length == 0;
            }
        }
    }

    public class GovernanceData: ExplorerObject
    {
        public GovernanceData(NexusData database, DataNode node): base(database)
        {
            Name = node.GetString("name");
            Value = node.GetString("value");
        }

        public string Name { get; private set; }
        public string Value { get; private set; }
    }

    public class NexusData
    {
        public string Name { get; private set; }

        private Dictionary<string, ChainData> _chains = new Dictionary<string, ChainData>();
        private Dictionary<string, TokenData> _tokens = new Dictionary<string, TokenData>();
        private Dictionary<string, PlatformData> _platforms = new Dictionary<string, PlatformData>();

        private Dictionary<Hash, BlockData> _blocks = new Dictionary<Hash, BlockData>();
        private Dictionary<Hash, TransactionData> _transactions = new Dictionary<Hash, TransactionData>();

        private Dictionary<Address, AccountData> _accounts = new Dictionary<Address, AccountData>();

        private List<GovernanceData> _governance = new List<GovernanceData>();

        public Address[] Masters { get; private set; }

        private string RESTurl;

        public IEnumerable<ChainData> Chains => _chains.Values;
        public IEnumerable<TokenData> Tokens => _tokens.Values;
        public IEnumerable<PlatformData> Platforms => _platforms.Values;
        public IEnumerable<GovernanceData> Governance => _governance;

        public ChainData RootChain => FindChainByName("main");

        private int updateCount;

        public NexusData(string RESTurl)
        {
            if (!RESTurl.EndsWith("/"))
            {
                RESTurl += "/";
            }
            this.RESTurl = RESTurl;
        }

        public bool Update()
        {
            var node = APIRequest("getNexus");
            if (node == null)
            {
                return false;
            }

            this.Name = node.GetString("name");

            var tokens = node.GetNode("tokens");
            foreach (var entry in tokens.Children)
            {
                var token = new TokenData(this, entry);
                if (!_tokens.ContainsKey(token.Symbol))
                {
                    Console.WriteLine("Detected new token: " + token.Name);
                    _tokens[token.Symbol] = token;
                }
            }
            
            var chains = node.GetNode("chains");
            foreach (var entry in chains.Children)
            {
                var chain = new ChainData(this, entry);
                if (_chains.ContainsKey(chain.Name))
                {
                    var height = chain.Height;
                    chain = _chains[chain.Name];
                    var diff = height - chain.Height;
                    if (diff > 0)
                    {
                        Console.WriteLine($"Detected {diff} new blocks on chain {chain.Name}");
                        chain.Grow((int)height);
                    }
                }
                else
                {
                    Console.WriteLine("Detected new chain: " + chain.Name);
                    _chains[chain.Name] = chain;
                }
            }

            var platforms = node.GetNode("platforms");
            if (platforms != null)
            {
                foreach (var entry in platforms.Children)
                {
                    var platform = new PlatformData(this, entry);
                    _platforms[platform.Name] = platform;
                }
            }

            var govNode = node.GetNode("governance");
            foreach (var entry in govNode.Children)
            {
                var gov = new GovernanceData(this, entry);
                _governance.Add(gov);
            }

            var masterNode = node.GetNode("masters");
            if (masterNode != null)
            {
                this.Masters = new Address[masterNode.ChildCount];
                int index = 0;
                foreach (var entry in masterNode.Children)
                {
                    Masters[index] = Address.FromText(entry.Value);
                    index++;
                }
            }
            else
            {
                this.Masters = new Address[0];
            }

            foreach (var chain in _chains.Values)
            {
                chain.UpdateBlocks();
            }

            if (updateCount == 0)
            {
                DoParallelRequests("Fetching master accounts...", Masters.Length, (index) =>
                {
                    FindAccount(Masters[index], false);
                });
            }

            updateCount++;

            return true;
        }

        public DataNode APIRequest(string path)
        {
            var url = RESTurl + path;
            //Console.WriteLine("Request: " + url);

            try
            {
                string contents;
                using (var wc = new System.Net.WebClient())
                {
                    contents = wc.DownloadString(url);
                }

                var node = LunarLabs.Parser.JSON.JSONReader.ReadFromString(contents);
                return node;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        public ChainData FindChainByName(string name)
        {
            if (_chains.ContainsKey(name))
            {
                return _chains[name];
            }

            return null;
        }

        internal ChainData FindChainByAddress(Address chainAddress)
        {
            foreach (var chain in _chains.Values)
            {
                if (chain.Address == chainAddress)
                {
                    return chain;
                }
            }

            return null;
        }

        internal IToken FindTokenBySymbol(string symbol)
        {
            if (_tokens.ContainsKey(symbol))
            {
                return _tokens[symbol];
            }

            return null;
        }

        public decimal ToDecimal(BigInteger amount, string symbol)
        {
            if (_tokens.ContainsKey(symbol))
            {
                var token = _tokens[symbol];
                return UnitConversion.ToDecimal(amount, token.Decimals);
            }

            return 0;
        }

        public TransactionData FindTransaction(ChainData chain, Hash txHash)
        {
            if (_transactions.ContainsKey(txHash))
            {
                return _transactions[txHash];
            }

            var node = APIRequest($"getTransaction?hashText={txHash}");
            var tx = new TransactionData(this, node);
            lock (_transactions)
            {
                _transactions[tx.Hash] = tx;
            }
            return tx;
        }

        internal BlockData FindBlockByHeight(ChainData chainData, int height)
        {
            var node = APIRequest($"getBlockByHeight?chainInput={chainData.Name}&height={height}");
            var block = new BlockData(this, node);
            lock (_blocks)
            {
                _blocks[block.Hash] = block;
            }
            return block;
        }

        internal BlockData FindBlockByHash(ChainData chainData, Hash hash)
        {
            lock (_blocks)
            {
                if (_blocks.ContainsKey(hash))
                {
                    return _blocks[hash];
                }
            }

            return null;
        }

        public AccountData FindAccount(Address address, bool canExpire)
        {
            AccountData account;

            if (_accounts.ContainsKey(address))
            {
                account = _accounts[address];
                
                if (account != null)
                {
                    var diff = DateTime.UtcNow - account.LastTime;
                    if (!canExpire || diff.TotalSeconds < 60)
                    {
                        return account;
                    }
                }
            }

            var node = APIRequest("getAccount?account=" + address.Text);

            if (node != null)
            {
                account = new AccountData(this, node);
                if (account.IsEmpty)
                {
                    return null;
                }

                _accounts[address] = account;
                return account;
            }

            return null;
        }

        public void DoParallelRequests(string description, int total, Action<int> fetcher)
        {
            Console.WriteLine(description);

            //var progress = new ProgressBar();

            var blockSize = 16;

            //int finished = 0;

            int offset = 0;
            while (total > 0)
            {
                var roundSize = Math.Min(blockSize, total);

                var tasks = new Task[roundSize];

                for (int i = 0; i < roundSize; i++)
                {
                    var index = i + offset;
                    tasks[i] = new Task(() =>
                    {
                        fetcher(index);
                        /*lock (progress)
                        {
                            finished++;
                            progress.Report(finished / (float)total);
                        }*/
                    });
                }

                foreach (var task in tasks)
                {
                    task.Start();
                }

                Task.WaitAll(tasks);

                offset += roundSize;
                total -= roundSize;
            }
        }
    }
}
