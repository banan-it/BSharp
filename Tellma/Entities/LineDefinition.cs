﻿using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tellma.Entities
{
    [EntityDisplay(Singular = "LineDefinition", Plural = "LineDefinitions")]
    public class LineDefinitionForSave<TEntry, TColumn, TStateReason, TGenerateParameter, TWorkflow> : EntityWithKey<int>
    {
        [Display(Name = "Code")]
        [Required]
        [StringLength(100)]
        [AlwaysAccessible]
        public string Code { get; set; }

        [MultilingualDisplay(Name = "Description", Language = Language.Primary)]
        [StringLength(1024)]
        [AlwaysAccessible]
        public string Description { get; set; }

        [MultilingualDisplay(Name = "Description", Language = Language.Secondary)]
        [StringLength(1024)]
        [AlwaysAccessible]
        public string Description2 { get; set; }

        [MultilingualDisplay(Name = "Description", Language = Language.Ternary)]
        [StringLength(1024)]
        [AlwaysAccessible]
        public string Description3 { get; set; }

        [MultilingualDisplay(Name = "TitleSingular", Language = Language.Primary)]
        [StringLength(100)]
        [AlwaysAccessible]
        public string TitleSingular { get; set; }

        [MultilingualDisplay(Name = "TitleSingular", Language = Language.Secondary)]
        [StringLength(100)]
        [AlwaysAccessible]
        public string TitleSingular2 { get; set; }

        [MultilingualDisplay(Name = "TitleSingular", Language = Language.Ternary)]
        [StringLength(100)]
        [AlwaysAccessible]
        public string TitleSingular3 { get; set; }

        [MultilingualDisplay(Name = "TitlePlural", Language = Language.Primary)]
        [Required]
        [StringLength(100)]
        [AlwaysAccessible]
        public string TitlePlural { get; set; }

        [MultilingualDisplay(Name = "TitlePlural", Language = Language.Secondary)]
        [StringLength(100)]
        [AlwaysAccessible]
        public string TitlePlural2 { get; set; }

        [MultilingualDisplay(Name = "TitlePlural", Language = Language.Ternary)]
        [StringLength(100)]
        [AlwaysAccessible]
        public string TitlePlural3 { get; set; }

        [Display(Name = "LineDefinition_AllowSelectiveSigning")]
        public bool? AllowSelectiveSigning { get; set; }

        [Display(Name = "LineDefinition_ViewDefaultsToForm")]
        public bool? ViewDefaultsToForm { get; set; }
        [MultilingualDisplay(Name = "LineDefinition_GenerateLabel", Language = Language.Primary)]
        [StringLength(50)]
        public string GenerateLabel { get; set; }

        [MultilingualDisplay(Name = "LineDefinition_GenerateLabel", Language = Language.Secondary)]
        [StringLength(50)]
        public string GenerateLabel2 { get; set; }

        [MultilingualDisplay(Name = "LineDefinition_GenerateLabel", Language = Language.Ternary)]
        [StringLength(50)]
        public string GenerateLabel3 { get; set; }

        [Display(Name = "LineDefinition_GenerateScript")]
        public string GenerateScript { get; set; }

        [Display(Name = "LineDefinition_PreprocessScript")]
        public string PreprocessScript { get; set; }

        [Display(Name = "LineDefinition_ValidateScript")]
        public string ValidateScript { get; set; }

        [Display(Name = "LineDefinition_Entries")]
        [ForeignKey(nameof(LineDefinitionEntry.LineDefinitionId))]
        public List<TEntry> Entries { get; set; }

        [Display(Name = "LineDefinition_Columns")]
        [ForeignKey(nameof(LineDefinitionColumn.LineDefinitionId))]
        public List<TColumn> Columns { get; set; }

        [Display(Name = "LineDefinition_StateReasons")]
        [ForeignKey(nameof(LineDefinitionStateReason.LineDefinitionId))]
        public List<TStateReason> StateReasons { get; set; }

        [Display(Name = "LineDefinition_GenerateParameters")]
        [ForeignKey(nameof(LineDefinitionGenerateParameter.LineDefinitionId))]
        public List<TGenerateParameter> GenerateParameters { get; set; }

        [Display(Name = "LineDefinition_Workflows")]
        [ForeignKey(nameof(Workflow.LineDefinitionId))]
        public List<TWorkflow> Workflows { get; set; }
    }

    public class LineDefinitionForSave : LineDefinitionForSave<LineDefinitionEntryForSave, LineDefinitionColumnForSave, LineDefinitionStateReasonForSave, LineDefinitionGenerateParameterForSave, WorkflowForSave>
    {
    }

    public class LineDefinition : LineDefinitionForSave<LineDefinitionEntry, LineDefinitionColumn, LineDefinitionStateReason, LineDefinitionGenerateParameter, Workflow>
    {
        public int? NotedRelationDefinitionId { get; set; }

        [Display(Name = "ModifiedBy")]
        public int? SavedById { get; set; }

        // For Query

        [ForeignKey(nameof(NotedRelationDefinitionId))]
        public RelationDefinition NotedRelationDefinition { get; set; }

        [Display(Name = "ModifiedBy")]
        [ForeignKey(nameof(SavedById))]
        public User SavedBy { get; set; }
    }
}
