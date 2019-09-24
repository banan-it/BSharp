﻿using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BSharp.Entities
{
    public class DocumentLineEntryForSave : EntityWithKey<int>
    {
        [AlwaysAccessible]
        public int? EntryNumber { get; set; }

        [AlwaysAccessible]
        [ChoiceList(new object[] { -1, 1 })]
        public byte? Direction { get; set; }

        [Display(Name = "DocumentLineEntry_Account")]
        public int? AccountId { get; set; }

        [Display(Name = "DocumentLineEntry_IfrsEntryClassification")]
        [StringLength(255, ErrorMessage = nameof(StringLengthAttribute))]
        public string IfrsEntryClassificationId { get; set; }

        [Display(Name = "DocumentLineEntry_Agent")]
        public int? AgentId { get; set; }

        [Display(Name = "DocumentLineEntry_ResponsibilityCenter")]
        public int? ResponsibilityCenterId { get; set; }

        [Display(Name = "DocumentLineEntry_Resource")]
        public int? ResourceId { get; set; }

        [Display(Name = "DocumentLineEntry_ResourcePick")]
        public int? ResourcePickId { get; set; }

        [Display(Name = "DocumentLineEntry_BatchCode")]
        [StringLength(255, ErrorMessage = nameof(StringLengthAttribute))]
        public string BatchCode { get; set; }

        public DateTime? DueDate { get; set; } // TODO

        [Display(Name = "DocumentLineEntry_MonetaryValue")]
        public decimal? MonetaryValue { get; set; }

        [Display(Name = "DocumentLineEntry_Mass")]
        public decimal? Mass { get; set; }

        [Display(Name = "DocumentLineEntry_Volume")]
        public decimal? Volume { get; set; }

        [Display(Name = "DocumentLineEntry_Area")]
        public decimal? Area { get; set; }

        [Display(Name = "DocumentLineEntry_Length")]
        public decimal? Length { get; set; }

        [Display(Name = "DocumentLineEntry_Time")]
        public decimal? Time { get; set; }

        [Display(Name = "DocumentLineEntry_Count")]
        public decimal? Count { get; set; }

        [Display(Name = "DocumentLineEntry_Value")]
        public decimal? Value { get; set; }
    }

    public class DocumentLineEntry : DocumentLineEntryForSave
    {
        [Display(Name = "CreatedAt")]
        public DateTimeOffset? CreatedAt { get; set; }

        [Display(Name = "CreatedBy")]
        public int? CreatedById { get; set; }

        [Display(Name = "ModifiedAt")]
        public DateTimeOffset? ModifiedAt { get; set; }

        [Display(Name = "ModifiedBy")]
        public int? ModifiedById { get; set; }

        // For Query

        [Display(Name = "DocumentLineEntry_Account")]
        [ForeignKey(nameof(AccountId))]
        public Account Account { get; set; }

        [Display(Name = "DocumentLineEntry_IfrsEntryClassification")]
        [ForeignKey(nameof(IfrsEntryClassificationId))]
        public IfrsEntryClassification IfrsEntryClassification { get; set; }

        [Display(Name = "DocumentLineEntry_Agent")]
        [ForeignKey(nameof(AgentId))]
        public Agent Agent { get; set; }

        [Display(Name = "DocumentLineEntry_ResponsibilityCenter")]
        [ForeignKey(nameof(ResponsibilityCenterId))]
        public ResponsibilityCenter ResponsibilityCenter { get; set; }

        [Display(Name = "DocumentLineEntry_Resource")]
        [ForeignKey(nameof(ResourceId))]
        public Resource Resource { get; set; }

        [Display(Name = "DocumentLineEntry_ResourcePick")]
        [ForeignKey(nameof(ResourcePickId))]
        public ResourcePick ResourcePick { get; set; }

        [Display(Name = "CreatedBy")]
        [ForeignKey(nameof(CreatedById))]
        public User CreatedBy { get; set; }

        [Display(Name = "ModifiedBy")]
        [ForeignKey(nameof(ModifiedById))]
        public User ModifiedBy { get; set; }
    }
}