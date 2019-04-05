using System.Collections.Generic;

namespace Carbonara.Models.Calculation
{
    public class TotalCalculationResult
    {
        public Dictionary<int, CalculationResult> CalculationPerYear;
        public List<Country.Country> AverageCO2EmissionPerCountry;

        public TotalCalculationResult()
        {
            if (CalculationPerYear == null)
                CalculationPerYear = new Dictionary<int, CalculationResult>();
        }
    }
}



