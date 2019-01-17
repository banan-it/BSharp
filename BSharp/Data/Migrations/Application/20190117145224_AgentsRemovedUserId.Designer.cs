﻿// <auto-generated />
using System;
using BSharp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BSharp.Data.Migrations.Application
{
    [DbContext(typeof(ApplicationContext))]
    [Migration("20190117145224_AgentsRemovedUserId")]
    partial class AgentsRemovedUserId
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "2.2.0-rtm-35687")
                .HasAnnotation("Relational:MaxIdentifierLength", 128)
                .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

            modelBuilder.Entity("BSharp.Data.Model.Custody", b =>
                {
                    b.Property<int>("TenantId");

                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("Address")
                        .HasMaxLength(1024);

                    b.Property<DateTimeOffset?>("BirthDateTime");

                    b.Property<string>("Code")
                        .HasMaxLength(255);

                    b.Property<DateTimeOffset>("CreatedAt");

                    b.Property<string>("CreatedBy")
                        .IsRequired()
                        .HasMaxLength(450);

                    b.Property<string>("CustodyType")
                        .IsRequired()
                        .HasMaxLength(255);

                    b.Property<bool>("IsActive")
                        .ValueGeneratedOnAdd()
                        .HasDefaultValue(true);

                    b.Property<DateTimeOffset>("ModifiedAt");

                    b.Property<string>("ModifiedBy")
                        .IsRequired()
                        .HasMaxLength(450);

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(255);

                    b.Property<string>("Name2")
                        .HasMaxLength(255);

                    b.HasKey("TenantId", "Id");

                    b.HasIndex("TenantId", "Code")
                        .IsUnique()
                        .HasFilter("[Code] IS NOT NULL");

                    b.ToTable("Custodies");

                    b.HasDiscriminator<string>("CustodyType").HasValue("Custody");
                });

            modelBuilder.Entity("BSharp.Data.Model.MeasurementUnit", b =>
                {
                    b.Property<int>("TenantId");

                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

                    b.Property<double>("BaseAmount");

                    b.Property<string>("Code")
                        .HasMaxLength(255);

                    b.Property<DateTimeOffset>("CreatedAt");

                    b.Property<string>("CreatedBy")
                        .IsRequired()
                        .HasMaxLength(450);

                    b.Property<bool>("IsActive")
                        .ValueGeneratedOnAdd()
                        .HasDefaultValue(true);

                    b.Property<DateTimeOffset>("ModifiedAt");

                    b.Property<string>("ModifiedBy")
                        .IsRequired()
                        .HasMaxLength(450);

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(255);

                    b.Property<string>("Name2")
                        .HasMaxLength(255);

                    b.Property<double>("UnitAmount");

                    b.Property<string>("UnitType")
                        .IsRequired()
                        .HasMaxLength(255);

                    b.HasKey("TenantId", "Id");

                    b.HasIndex("TenantId", "Code")
                        .IsUnique()
                        .HasFilter("[Code] IS NOT NULL");

                    b.ToTable("MeasurementUnits");
                });

            modelBuilder.Entity("BSharp.Data.Model.Agent", b =>
                {
                    b.HasBaseType("BSharp.Data.Model.Custody");

                    b.Property<string>("AgentType")
                        .IsRequired()
                        .HasMaxLength(255);

                    b.Property<string>("Gender")
                        .HasConversion(new ValueConverter<string, string>(v => default(string), v => default(string), new ConverterMappingHints(size: 1)));

                    b.Property<bool>("IsRelated")
                        .ValueGeneratedOnAdd()
                        .HasDefaultValue(false);

                    b.Property<string>("TaxIdentificationNumber")
                        .HasMaxLength(255);

                    b.Property<string>("Title")
                        .HasMaxLength(255);

                    b.Property<string>("Title2")
                        .HasMaxLength(255);

                    b.HasDiscriminator().HasValue("Agent");
                });
#pragma warning restore 612, 618
        }
    }
}
