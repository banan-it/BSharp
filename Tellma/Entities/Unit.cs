﻿using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tellma.Entities
{
    [StrongEntity]
    [EntityDisplay(Singular = "Unit", Plural = "Units")]
    public class UnitForSave : EntityWithKey<int>
    {
        [Display(Name = "Unit_UnitType")]
        [Required]
        [NotNull]
        [ChoiceList(new object[] { "Pure", "Time", "Distance", "Count", "Mass", "Volume" },
            new string[] { "Unit_Pure", "Unit_Time", "Unit_Distance", "Unit_Count", "Unit_Mass", "Unit_Volume" })]
        public string UnitType { get; set; }

        [MultilingualDisplay(Name = "Name", Language = Language.Primary)]
        [Required]
        [NotNull]
        [StringLength(50)]
        [AlwaysAccessible]
        public string Name { get; set; }

        [MultilingualDisplay(Name = "Name", Language = Language.Secondary)]
        [StringLength(50)]
        [AlwaysAccessible]
        public string Name2 { get; set; }

        [MultilingualDisplay(Name = "Name", Language = Language.Ternary)]
        [StringLength(50)]
        [AlwaysAccessible]
        public string Name3 { get; set; }

        [Display(Name = "Code")]
        [StringLength(50)]
        [AlwaysAccessible]
        public string Code { get; set; }

        [MultilingualDisplay(Name = "Description", Language = Language.Primary)]
        [Required]
        [NotNull]
        [StringLength(255)]
        [AlwaysAccessible]
        public string Description { get; set; }

        [MultilingualDisplay(Name = "Description", Language = Language.Secondary)]
        [StringLength(255)]
        [AlwaysAccessible]
        public string Description2 { get; set; }

        [MultilingualDisplay(Name = "Description", Language = Language.Ternary)]
        [StringLength(255)]
        [AlwaysAccessible]
        public string Description3 { get; set; }

        [Display(Name = "Unit_UnitAmount")]
        [Required]
        [NotNull]
        public double? UnitAmount { get; set; }

        [Display(Name = "Unit_BaseAmount")]
        [Required]
        [NotNull]
        public double? BaseAmount { get; set; }
    }

    public class Unit : UnitForSave
    {
        [Display(Name = "IsActive")]
        [NotNull]
        [AlwaysAccessible]
        public bool? IsActive { get; set; }

        [Display(Name = "CreatedAt")]
        [NotNull]
        public DateTimeOffset? CreatedAt { get; set; }

        [Display(Name = "CreatedBy")]
        [NotNull]
        public int? CreatedById { get; set; }

        [Display(Name = "ModifiedAt")]
        [NotNull]
        public DateTimeOffset? ModifiedAt { get; set; }

        [Display(Name = "ModifiedBy")]
        [NotNull]
        public int? ModifiedById { get; set; }

        // For Query

        [Display(Name = "CreatedBy")]
        [ForeignKey(nameof(CreatedById))]
        public User CreatedBy { get; set; }

        [Display(Name = "ModifiedBy")] 
        [ForeignKey(nameof(ModifiedById))]
        public User ModifiedBy { get; set; }
    }
}
