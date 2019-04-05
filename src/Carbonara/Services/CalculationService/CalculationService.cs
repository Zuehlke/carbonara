using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Carbonara.Models.Country;
using Carbonara.Models.Calculation;
using Carbonara.Models.PoolHashRateDistribution;
using Carbonara.Models.PoolTypeHashRateDistribution;
using Carbonara.Services.BlockParametersService;
using Carbonara.Services.CountryCo2EmissionService;
using Carbonara.Services.PoolHashRateService;
using Carbonara.Models.MiningHardware;
using Carbonara.Services.HashRatePerPoolService;
using Carbonara.Models.Formula;
using Carbonara.Services.MiningHardwareService;
using Carbonara.Services.NetworkHashRateService;

namespace Carbonara.Services.CalculationService
{
    public class CalculationService : ICalculationService
    {
        private readonly IBlockParametersService _blockParametersService;
        private readonly INetworkHashRateService _networkHashRateService;
        private readonly IPoolHashRateService _poolHashRateService;
        private readonly ICountryCo2EmissionService _countryCo2EmissionService;
        private readonly IMiningHardwareService _miningHardwareService;
        private readonly IHashRatePerPoolService _hashRatePerPoolService;

        public CalculationService(
            IBlockParametersService blockParametersService,
            INetworkHashRateService networkHashRateService,
            IPoolHashRateService poolHashRateService,
            ICountryCo2EmissionService countryCo2EmissionService,
            IMiningHardwareService miningHardwareService,
            IHashRatePerPoolService hashRatePerPoolService)
        {
            _blockParametersService = blockParametersService;
            _networkHashRateService = networkHashRateService;
            _poolHashRateService = poolHashRateService;
            _countryCo2EmissionService = countryCo2EmissionService;
            _miningHardwareService = miningHardwareService;
            _hashRatePerPoolService = hashRatePerPoolService;
        }

        public async Task<TotalCalculationResult> Calculate(string txHash, string hashingAlg, string countryToUseForCo2EmissionAverage)
        {
            var result = new TotalCalculationResult();

            var transactionBlockParameters = await _blockParametersService.GetBlockParameters(txHash);

            var hashRateDistributionPerPool = await _poolHashRateService.GetPoolHashRateDistributionForTxDateAsync(transactionBlockParameters.TimeOfBlockMining);
            var geoDistributionOfHashratePerPoolType = await _hashRatePerPoolService.GetHashRatePerPoolAsync();

            var countriesWithAvgCo2Emission = await _countryCo2EmissionService.GetCountriesCo2EmissionAsync();
            result.AverageCo2EmissionPerCountryInKg = countriesWithAvgCo2Emission;

            var fullEnergyConsumptionPerTransactionInKWHPerYear =
                await this.CalculateTheFullEnergyConsumptionPerTransactionPerYear(transactionBlockParameters);

            foreach (var year in fullEnergyConsumptionPerTransactionInKWHPerYear.Keys)
            {
                var calculationResultForYear = new CalculationResult();

                var energyConsumptionPerPool =
                    this.DistributeEnergyPerPoolParticipationInTheHashRate(fullEnergyConsumptionPerTransactionInKWHPerYear[year], hashRateDistributionPerPool);

                var energyConsumptionPerCountry =
                    this.DistributeEnergyUsedByPoolsPerCountry(energyConsumptionPerPool, geoDistributionOfHashratePerPoolType);

                var co2EmissionPerCountry =
                    this.TranslateEnergyEmissionPerCountryToCo2EmissionPerCountry(energyConsumptionPerCountry, countriesWithAvgCo2Emission, countryToUseForCo2EmissionAverage);

                var worldWideEmission = co2EmissionPerCountry.Sum(c => c.Co2Emission);

                calculationResultForYear.EnergyConsumptionPerCountryInKWh = energyConsumptionPerCountry;
                calculationResultForYear.FullCo2EmissionInKg = worldWideEmission;

                result.CalculationPerYear.Add(year, calculationResultForYear);
            }

            return result;
        }

        private async Task<Dictionary<int, decimal>> CalculateTheFullEnergyConsumptionPerTransactionPerYear(
            BlockParameters blockParameters)
        {
            var networkHashRateInTHs = await _networkHashRateService.GetDailyHashRateInPastAsync(blockParameters.TimeOfBlockMining); // Provided in TH/s
            var energyConsumptionPerYear = new Dictionary<int, decimal>();

            var devices = await _miningHardwareService.GetHardwareByMiningAlgorithm(MiningAlgorithm.SHA256);

            foreach (var device in devices)
            {
                var value = CalculateFullEnergyConsumptionPerTransactionInKwhByDevice(blockParameters, networkHashRateInTHs, device);
                energyConsumptionPerYear.Add(device.ProductionYear, value);
            }
            return energyConsumptionPerYear;
        }

        private static decimal CalculateFullEnergyConsumptionPerTransactionInKwhByDevice(BlockParameters blockParameters, decimal networkHashRateInTHs, MiningDevice device)
        {
            var avgMachineHashRateInTHs = device.HashRate / 1000000000000m; // Average hashrate of a machine converted to TH/s from H/s
            var avgMachineEnergyConsumptionInKWH = device.PowerConsumption / 1000m; // Average machine energy consumption converted to KW/h from W/h

            var noOfMachinesDoingTheMinning = networkHashRateInTHs / avgMachineHashRateInTHs; // The number of machines that were doing the mining for that block, under the assumption that all of them mined
            var energyConsumptionPerMachinePerBlockInKWH = avgMachineEnergyConsumptionInKWH * blockParameters.BlockTimeInSeconds / 3600m;

            var fullEnergyConsumptionPerTransactionInKWH = noOfMachinesDoingTheMinning * energyConsumptionPerMachinePerBlockInKWH / blockParameters.NumberOfTransactionsInBlock;
            return fullEnergyConsumptionPerTransactionInKWH;
        }

        private List<EnergyConsumptionPerPool> DistributeEnergyPerPoolParticipationInTheHashRate(
            decimal fullEnergyForTransaction,
            List<Pool> hashRateDistributionPerPool)
        {
            var energyConsumptionPerPoolPerTransactionInKwh = new List<EnergyConsumptionPerPool>();

            foreach (var pool in hashRateDistributionPerPool)
            {
                var poolEnergyConsumption = fullEnergyForTransaction * pool.Percent / 100m;

                var energyConsumptionPerPool = new EnergyConsumptionPerPool()
                {
                    Pool = pool,
                    EnergyConsumption = poolEnergyConsumption
                };

                energyConsumptionPerPoolPerTransactionInKwh.Add(energyConsumptionPerPool);
            }

            return energyConsumptionPerPoolPerTransactionInKwh;
        }

        private List<EnergyConsumptionPerCountryInKWh> DistributeEnergyUsedByPoolsPerCountry(
            List<EnergyConsumptionPerPool> energyConsumptionPerPool,
            List<PoolTypeHashRateDistribution> geoDistributionOfHashratePerPoolType)
        {
            var energyConsumptionPerCountryPerTransactionInKwh = new List<EnergyConsumptionPerCountryInKWh>();

            foreach (var energyPerPool in energyConsumptionPerPool)
            {
                var geoDistributionOfHashRateForSinglePool = geoDistributionOfHashratePerPoolType
                    .First(p => p.PoolType == energyPerPool.Pool.PoolType).DistributionPerCountry;

                foreach (var geoPoolDistribution in geoDistributionOfHashRateForSinglePool)
                {
                    var consumptionPerCountry = energyConsumptionPerCountryPerTransactionInKwh.FirstOrDefault(c => c.CountryCode == geoPoolDistribution.CountryCode);

                    if (consumptionPerCountry != null)
                    {
                        consumptionPerCountry.EnergyConsumptionInKWh += energyPerPool.EnergyConsumption * geoPoolDistribution.Percentage / 100m;
                    }
                    else
                    {
                        energyConsumptionPerCountryPerTransactionInKwh.Add(
                            new EnergyConsumptionPerCountryInKWh
                            {
                                CountryCode = geoPoolDistribution.CountryCode,
                                EnergyConsumptionInKWh = energyPerPool.EnergyConsumption * geoPoolDistribution.Percentage / 100m
                            }
                        );
                    }
                }
            }

            return energyConsumptionPerCountryPerTransactionInKwh;
        }

        private List<Co2EmissionPerCountry> TranslateEnergyEmissionPerCountryToCo2EmissionPerCountry(
            List<EnergyConsumptionPerCountryInKWh> energyConsumptionPerCountry,
            List<Country> countriesWithAvgCo2Emission,
            string countryToUseForCo2EmissionAverage)
        {
            var co2PerCountry = new List<Co2EmissionPerCountry>();

            foreach (var consumptionPerCountry in energyConsumptionPerCountry)
            {
                // Either use the user provided country for avg emissions or use avg emissions per country
                var avgEmissionPerEnergyInGrams = String.IsNullOrEmpty(countryToUseForCo2EmissionAverage) ?
                    countriesWithAvgCo2Emission.First(c => c.CountryCode == consumptionPerCountry.CountryCode).Co2EmissionInKg :
                    countriesWithAvgCo2Emission.First(c => c.CountryCode == countryToUseForCo2EmissionAverage).Co2EmissionInKg;

                co2PerCountry.Add(
                    new Co2EmissionPerCountry
                    {
                        CountryCode = consumptionPerCountry.CountryCode,
                        Co2Emission = consumptionPerCountry.EnergyConsumptionInKWh * avgEmissionPerEnergyInGrams / 1000m
                    }
                );
            }

            return co2PerCountry;
        }
    }
}