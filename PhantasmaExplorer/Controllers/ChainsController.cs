﻿using System.Collections.Generic;
using Phantasma.Explorer.Infrastructure.Interfaces;
using Phantasma.Explorer.ViewModels;

namespace Phantasma.Explorer.Controllers
{
    public class ChainsController
    {
        public IRepository Repository { get; set; } //todo interface

        public ChainsController(IRepository repo)
        {
            Repository = repo;
        }

        public List<ChainViewModel> GetChains()
        {
            var repoChains = Repository.GetAllChains();
            var chainList = new List<ChainViewModel>();

            foreach (var repoChain in repoChains)
            {
                var blockList = new List<BlockViewModel>();
                var lastBlocks = Repository.GetBlocks(repoChain.Address.Text);

                foreach (var block in lastBlocks)
                {
                    blockList.Add(BlockViewModel.FromBlock(block));
                }

                chainList.Add(ChainViewModel.FromChain(repoChain, blockList));
            }

            return chainList;
        }

        public ChainViewModel GetChain(string chainInput)
        {
            var repoChain = Repository.GetChain(chainInput);
            var blockList = new List<BlockViewModel>();
            var lastBlocks = Repository.GetBlocks(repoChain.Address.Text);

            foreach (var block in lastBlocks)
            {
                blockList.Add(BlockViewModel.FromBlock(block));
            }

            return ChainViewModel.FromChain(repoChain, blockList);
        }
    }
}