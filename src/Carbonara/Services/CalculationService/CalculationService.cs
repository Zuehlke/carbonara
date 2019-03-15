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

        public async Task<CalculationResult> Calculate(string txHash, int minningGearYear, string hashingAlg, string countryToUseForCo2EmissionAverage)
        {
            var result = new CalculationResult();

            var transactionBlockParameters = await _blockParametersService.GetBlockParameters(txHash);

            var fullEnergyConsumptionPerTransactionInKWH =
                await this.CalculateTheFullEnergyConsumptionPerTransaction(transactionBlockParameters, minningGearYear);

            var hashRateDistributionPerPool = await _poolHashRateService.GetPoolHashRateDistributionForTxDateAsync(transactionBlockParameters.TimeOfBlockMining);

            var energyConsumptionPerPool =
                this.DistributeEnergyPerPoolParticipationInTheHashRate(fullEnergyConsumptionPerTransactionInKWH, hashRateDistributionPerPool);

            var geoDistributionOfHashratePerPoolType = await _hashRatePerPoolService.GetHashRatePerPoolAsync();

            var energyConsumptionPerCountry =
                this.DistributeEnergyUsedByPoolsPerCountry(energyConsumptionPerPool, geoDistributionOfHashratePerPoolType);

            var countriesWithAvgCo2Emission = await _countryCo2EmissionService.GetCountriesCo2EmissionAsync();

            var co2EmissionPerCountry =
                this.TranslateEnergyEmissionPerCountryToCo2EmissionPerCountry(energyConsumptionPerCountry, countriesWithAvgCo2Emission, countryToUseForCo2EmissionAverage);

            var worldWideEmission = co2EmissionPerCountry.Sum(c => c.Co2Emission);

            result.EnergyConsumptionPerCountry = energyConsumptionPerCountry;
            result.AverageEmissionPerCountry = countriesWithAvgCo2Emission;
            result.FullCo2Emission = worldWideEmission;

            return result;
        }

        private async Task<decimal> CalculateTheFullEnergyConsumptionPerTransaction(
            BlockParameters blockParameters,
            int minningGearYear)
        {
            var networkHashRateInTHs = await _networkHashRateService.GetDailyHashRateInPastAsync(blockParameters.TimeOfBlockMining); // Provided in TH/s

            var hardware = await _miningHardwareService.GetHardwareByAlgorithmAndYear(MiningAlgorithm.SHA256, minningGearYear);
            var avgMachineHashRateInTHs = hardware.First().HashRate / 1000000000000m; // Average hashrate of a machine converted to TH/s from H/s
            var avgMachineEnergyConsumptionInKWH = hardware.First().PowerConsumption / 1000m; // Average machine energy consumption converted to KW/h from W/h

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

        private List<EnergyConsumptionPerCountry> DistributeEnergyUsedByPoolsPerCountry(
            List<EnergyConsumptionPerPool> energyConsumptionPerPool,
            List<PoolTypeHashRateDistribution> geoDistributionOfHashratePerPoolType)
        {
            var energyConsumptionPerCountryPerTransactionInKwh = new List<EnergyConsumptionPerCountry>();

            foreach (var energyPerPool in energyConsumptionPerPool)
            {
                var geoDistributionOfHashRateForSinglePool = geoDistributionOfHashratePerPoolType
                    .First(p => p.PoolType == energyPerPool.Pool.PoolType).DistributionPerCountry;

                foreach (var geoPoolDistribution in geoDistributionOfHashRateForSinglePool)
                {
                    var consumptionPerCountry = energyConsumptionPerCountryPerTransactionInKwh.FirstOrDefault(c => c.CountryCode == geoPoolDistribution.CountryCode);

                    if (consumptionPerCountry != null)
                    {
                        consumptionPerCountry.EnergyConsumption += energyPerPool.EnergyConsumption * geoPoolDistribution.Percentage / 100m;
                    }
                    else
                    {
                        energyConsumptionPerCountryPerTransactionInKwh.Add(
                            new EnergyConsumptionPerCountry
                            {
                                CountryCode = geoPoolDistribution.CountryCode,
                                EnergyConsumption = energyPerPool.EnergyConsumption * geoPoolDistribution.Percentage / 100m
                            }
                        );
                    }
                }
            }

            return energyConsumptionPerCountryPerTransactionInKwh;
        }

        private List<Co2EmissionPerCountry> TranslateEnergyEmissionPerCountryToCo2EmissionPerCountry(
            List<EnergyConsumptionPerCountry> energyConsumptionPerCountry,
            List<Country> countriesWithAvgCo2Emission,
            string countryToUseForCo2EmissionAverage)
        {
            var co2PerCountry = new List<Co2EmissionPerCountry>();

            foreach (var consumptionPerCountry in energyConsumptionPerCountry)
            {
                // Either use the user provided country for avg emissions or use avg emissions per country
                var avgEmissionPerEnergyInGrams = String.IsNullOrEmpty(countryToUseForCo2EmissionAverage) ?
                    countriesWithAvgCo2Emission.First(c => c.CountryCode == consumptionPerCountry.CountryCode).Co2Emission :
                    countriesWithAvgCo2Emission.First(c => c.CountryCode == countryToUseForCo2EmissionAverage).Co2Emission;

                co2PerCountry.Add(
                    new Co2EmissionPerCountry
                    {
                        CountryCode = consumptionPerCountry.CountryCode,
                        Co2Emission = consumptionPerCountry.EnergyConsumption * avgEmissionPerEnergyInGrams / 1000m
                    }
                );
            }

            return co2PerCountry;
        }
    }
}