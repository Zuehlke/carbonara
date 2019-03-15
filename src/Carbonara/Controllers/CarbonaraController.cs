using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Carbonara.Models;
using Carbonara.Models.MiningHardware;
using Carbonara.Services;
using Carbonara.Services.ChartService;
using Carbonara.Services.CalculationService;
using Carbonara.Services.MiningHardwareService;
using Microsoft.AspNetCore.Mvc;

namespace Carbonara.Controllers
{
    [Route("api/[controller]")]
    public class CarbonaraController : ControllerBase
    {
        private readonly ICalculationService _calculationService;
        private readonly IMiningHardwareService _miningHardwareService;
        private readonly IChartService _chartService;

        public CarbonaraController(
            ICalculationService calculationService,
            IMiningHardwareService miningHardwareService,
            IChartService chartService)
        {
            _calculationService = calculationService;
            _miningHardwareService = miningHardwareService;
            _chartService = chartService;
        }

        /// <summary>
        /// Get years selection for approximation of miners hashrate\energy consumption
        /// </summary>
        /// <response code="200">List of integers representing years</response>
        [HttpGet("MinningGearYearsSelection")]
        public async Task<IActionResult> GetMinningGearYearsSelection()
        {
            var miningDevices = await _miningHardwareService.GetAll();

            var years = miningDevices
                .Select(m => m.ProductionYear)
                .OrderByDescending(y => y);

            return Ok(years);
        }

        /// <summary>
        /// Get energy standards selection for approximation of CO2 emmissions per KWH
        /// </summary>
        /// <response code="200">List of strings representing countries with the approximate CO2 consumption</response>
        [HttpGet("CountriesCO2EmissionSelection")]
        public async Task<IActionResult> GetCountriesCO2EmissionSelection()
        {
            var countries = await Task.FromResult(new List<string> { "CN", "US", "EU" });
            return Ok(countries);
        }

        /// <summary>
        /// Get the list of transaction hashes for a given bitcoin address
        /// </summary>
        /// <param name="bitcoinAddress">Bitcoin address for which to provide the hashes (Address can be base58 or hash160)</param>
        /// <response code="200">List of strings representing tx hashes for a given address</response>
        [HttpGet("TransactionList")]
        public async Task<IActionResult> GetFormulaParametersAsync(
            [FromQuery(Name = "BitcoinAddress")]string bitcoinAddress)
        {
            
            var txHashes = await Task.FromResult(
                new List<string> { 
                    "d99439e228bc0cb2199eaaaa2303ef4c7fd85fbd529070ba175f86252c8577ce", 
                    "c9d750df6d9e2e86d9b2ceedba942d5711a6f31caf5743f373f1d52d00e2bbf5", 
                    "b587f4573b70a43d1091f32345e722d10144b33c9f2dcf7171d952a414021a5d" });
            return Ok(txHashes);
        }

        /// <summary>
        /// Get the CO2 emission for a given transaction hash
        /// </summary>
        /// <param name="txHash">transaction hash</param>
        /// <param name="minningGearYear">(Optional) Year for which the minning gear hashrate\energy consumtion approximation should be taken into account.
        /// If not provided, defaults to 2013 Antminer S9 </param>
        /// <param name="hashingAlgorithm">(Optional) Hashing alg to be used for the minning gear approximation.
        /// Currently ignored and defaults to SHA256 </param>
        /// <param name="cO2EmissionCountry">(Optional) Country for which the CO2 emission per KWH appoximation should be taken into account.</param>
        /// <response code="200">Returns an approximation of the CO2 emmission in KG for the given transaction hash </response>
        [HttpGet("Calculation")]
        public async Task<IActionResult> GetCalculationAsync(
            [FromQuery(Name = "TxHash")]string txHash,
            [FromQuery(Name = "MinningGearYear")]int minningGearYear = 2013,
            [FromQuery(Name="HashingAlgorithm")]string hashingAlgorithm = "0",
            [FromQuery(Name = "CO2EmissionCountry")]string cO2EmissionCountry = null)
        {
            var result = await _calculationService.Calculate(txHash, minningGearYear, hashingAlgorithm, cO2EmissionCountry);
            return Ok(result);
        }

        /// <summary>
        /// Get bitcoin co2 emission (currently mocked) and price chart
        /// </summary>
        /// <response code="200">Returns co2 emission (currently mocked) and price chart </response>
        [HttpGet("Charts")]
        public async Task<IActionResult> GetBitcoinChartsAsync()
        {
            var bitcoinCharts = await _chartService.GetBitcoinChartsAsync();
            return Ok(bitcoinCharts);
        }
    }
}