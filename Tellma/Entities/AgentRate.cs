﻿using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tellma.Entities
{
    [EntityDisplay(Singular = "AgentRate", Plural = "AgentRates")]
    public class AgentRateForSave : EntityWithKey<int>
    {
        [Display(Name = "AgentRate_Resource")]
        public int? ResourceId { get; set; }

        [Display(Name = "AgentRate_Unit")]
        public int? UnitId { get; set; }

        [Display(Name = "AgentRate_Rate")]
        public decimal? Rate { get; set; }

        [Display(Name = "AgentRate_Currency")]
        [StringLength(3)]
        public string CurrencyId { get; set; }
    }

    public class AgentRate : AgentRateForSave
    {
        public int? AgentId { get; set; }

        [Display(Name = "AgentRate_Resource")]
        [ForeignKey(nameof(ResourceId))]
        public Resource Resource { get; set; }

        [Display(Name = "AgentRate_Unit")]
        [ForeignKey(nameof(UnitId))]
        public Unit Unit { get; set; }

        [Display(Name = "AgentRate_Currency")]
        [ForeignKey(nameof(CurrencyId))]
        public Currency Currency { get; set; }
    }
}
