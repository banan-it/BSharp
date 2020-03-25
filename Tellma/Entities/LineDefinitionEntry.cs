﻿using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tellma.Entities
{
    public class LineDefinitionEntryForSave : EntityWithKey<int>
    {
        public short? Direction { get; set; }

        [AlwaysAccessible]
        public int? AccountTypeParentId { get; set; }

        public string AgentDefinitionId { get; set; }

        public string NotedAgentDefinitionId { get; set; }

        public int? EntryTypeId { get; set; }
    }

    public class LineDefinitionEntry : LineDefinitionEntryForSave
    {
        [AlwaysAccessible]
        public int? Index { get; set; }

        [AlwaysAccessible]
        public bool? AccountTypeParentIsResourceClassification { get; set; }

        [AlwaysAccessible]
        public int? EntryTypeParentId { get; set; }

        public string LineDefinitionId { get; set; }

        [Display(Name = "ModifiedBy")]
        public int? SavedById { get; set; }

        // For Query

        [ForeignKey(nameof(AgentDefinitionId))]
        public AgentDefinition AgentDefinition { get; set; }

        [ForeignKey(nameof(NotedAgentDefinitionId))]
        public AgentDefinition NotedAgentDefinition { get; set; }

        [Display(Name = "ModifiedBy")]
        [ForeignKey(nameof(SavedById))]
        public User SavedBy { get; set; }
    }
}
