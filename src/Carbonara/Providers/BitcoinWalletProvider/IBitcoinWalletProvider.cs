using System.Collections.Generic;
using System.Threading.Tasks;
using Carbonara.Models;
using Carbonara.Services;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Carbonara.Providers
{
    public interface IBitcoinWalletProvider : IWalletProvider
    {
        Task<BitcoinWalletInformation> GetInformation(string address);
    }
}