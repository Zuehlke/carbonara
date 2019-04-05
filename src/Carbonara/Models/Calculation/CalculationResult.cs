using System.Collections.Generic;

namespace Carbonara.Models.Calculation 
{
    public class CalculationResult
    {
        public decimal FullCo2EmissionInKg;
        
        public List<EnergyConsumptionPerCountryInKWh> EnergyConsumptionPerCountryInKWh;
    }
}