/*============================================================================
  File:     instawdb.sql

  Summary:  Creates the AdventureWorks sample database. Run this on
  any version of SQL Server (2008R2 or later) to get AdventureWorks for your
  current version. 

  Date:     October 26, 2017
  Updated:  November 14, 2025

------------------------------------------------------------------------------
  This file is part of the Microsoft SQL Server Code Samples.

  Copyright (C) Microsoft Corporation.  All rights reserved.

  This source code is intended only as a supplement to Microsoft
  Development Tools and/or on-line documentation.  See these other
  materials for detailed information regarding Microsoft code samples.

  All data in this database is fictitious.
 
  THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY
  KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
  IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
  PARTICULAR PURPOSE.
============================================================================*/

/*
 * Vendored for JauntyQ (T5 part C): the DDL portion of the canonical
 * instawdb.sql from microsoft/sql-server-samples (MIT), taken verbatim.
 * Microsoft publishes AdventureWorksLT only as a .bak, so per the approved
 * plan this is the full AdventureWorks OLTP schema, schema only: the
 * DatabaseLog/ErrorLog tables, data types, pre-table functions, schemas,
 * XML schema collections, tables, primary keys, indexes, XML indexes and
 * foreign keys. Omitted: database creation, SQLCMD directives, BULK INSERT
 * data loads, full-text catalog/indexes (the test container has no
 * full-text service), the DDL/table triggers, views, database functions,
 * stored procedures and extended properties. Nothing was hand-edited
 * inside the kept sections.
 */
-- Create table to store database object creation messages
-- *** WARNING:  THIS TABLE IS INTENTIONALLY A HEAP - DO NOT ADD A PRIMARY KEY ***
CREATE TABLE [dbo].[DatabaseLog](
    [DatabaseLogID] [int] IDENTITY (1, 1) NOT NULL,
    [PostTime] [datetime] NOT NULL,
    [DatabaseUser] [sysname] NOT NULL,
    [Event] [sysname] NOT NULL,
    [Schema] [sysname] NULL,
    [Object] [sysname] NULL,
    [TSQL] [nvarchar](max) NOT NULL,
    [XmlEvent] [xml] NOT NULL
) ON [PRIMARY];
GO
GO

-- Create table to store error information
CREATE TABLE [dbo].[ErrorLog](
    [ErrorLogID] [int] IDENTITY (1, 1) NOT NULL,
    [ErrorTime] [datetime] NOT NULL CONSTRAINT [DF_ErrorLog_ErrorTime] DEFAULT (GETDATE()),
    [UserName] [sysname] NOT NULL,
    [ErrorNumber] [int] NOT NULL,
    [ErrorSeverity] [int] NULL,
    [ErrorState] [int] NULL,
    [ErrorProcedure] [nvarchar](126) NULL,
    [ErrorLine] [int] NULL,
    [ErrorMessage] [nvarchar](4000) NOT NULL
) ON [PRIMARY];
GO

ALTER TABLE [dbo].[ErrorLog] WITH CHECK ADD
    CONSTRAINT [PK_ErrorLog_ErrorLogID] PRIMARY KEY CLUSTERED
    (
        [ErrorLogID]
    )  ON [PRIMARY];
GO

-- ****************************************
-- Create Data Types
-- ****************************************
PRINT '';
PRINT '*** Creating Data Types';
GO

CREATE TYPE [AccountNumber] FROM nvarchar(15) NULL;
CREATE TYPE [Flag] FROM bit NOT NULL;
CREATE TYPE [NameStyle] FROM bit NOT NULL;
CREATE TYPE [Name] FROM nvarchar(50) NULL;
CREATE TYPE [OrderNumber] FROM nvarchar(25) NULL;
CREATE TYPE [Phone] FROM nvarchar(25) NULL;
GO

-- ******************************************************
-- Add pre-table database functions.
-- ******************************************************
PRINT '';
PRINT '*** Creating Pre-Table Database Functions';
GO

CREATE FUNCTION [dbo].[ufnLeadingZeros](
    @Value int
)
RETURNS varchar(8)
WITH SCHEMABINDING
AS
BEGIN
    DECLARE @ReturnValue varchar(8);

    SET @ReturnValue = CONVERT(varchar(8), @Value);
    SET @ReturnValue = REPLICATE('0', 8 - DATALENGTH(@ReturnValue)) + @ReturnValue;

    RETURN (@ReturnValue);
END;
GO

-- ******************************************************
-- Create database schemas
-- ******************************************************
PRINT '';
PRINT '*** Creating Database Schemas';
GO

CREATE SCHEMA [HumanResources] AUTHORIZATION [dbo];
GO

CREATE SCHEMA [Person] AUTHORIZATION [dbo];
GO

CREATE SCHEMA [Production] AUTHORIZATION [dbo];
GO

CREATE SCHEMA [Purchasing] AUTHORIZATION [dbo];
GO

CREATE SCHEMA [Sales] AUTHORIZATION [dbo];
GO

-- ****************************************
-- Create XML schemas
-- ****************************************
PRINT '';
PRINT '*** Creating XML Schemas';
GO

-- Create AdditionalContactInfo schema
PRINT '';
PRINT 'Create AdditionalContactInfo schema';
GO

CREATE XML SCHEMA COLLECTION [Person].[AdditionalContactInfoSchemaCollection] AS
'<?xml version="1.0" encoding="UTF-8"?>
<xsd:schema targetNamespace="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ContactInfo"
    xmlns="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ContactInfo"
    elementFormDefault="qualified"
    xmlns:xsd="http://www.w3.org/2001/XMLSchema" >
    <!-- the following imports are not needed. They simply provide readability -->

    <xsd:import
        namespace="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ContactRecord" />

    <xsd:import
        namespace="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ContactTypes" />

    <xsd:element name="AdditionalContactInfo" >
        <xsd:complexType mixed="true" >
            <xsd:sequence>
                <xsd:any processContents="strict"
                    namespace="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ContactRecord
                        http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ContactTypes"
                        minOccurs="0" maxOccurs="unbounded" />
            </xsd:sequence>
        </xsd:complexType>
    </xsd:element>
</xsd:schema>';
GO

ALTER XML SCHEMA COLLECTION [Person].[AdditionalContactInfoSchemaCollection] ADD
'<?xml version="1.0" encoding="UTF-8"?>
<xsd:schema targetNamespace="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ContactRecord"
    elementFormDefault="qualified"
    xmlns="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ContactRecord"
    xmlns:xsd="http://www.w3.org/2001/XMLSchema" >

    <xsd:element name="ContactRecord" >
        <xsd:complexType mixed="true" >
            <xsd:choice minOccurs="0" maxOccurs="unbounded" >
                <xsd:any processContents="strict" 
                    namespace="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ContactTypes" />
            </xsd:choice>
            <xsd:attribute name="date" type="xsd:date" />
        </xsd:complexType>
    </xsd:element>
</xsd:schema>';
GO

ALTER XML SCHEMA COLLECTION [Person].[AdditionalContactInfoSchemaCollection] ADD
'<?xml version="1.0" encoding="UTF-8"?>
<xsd:schema targetNamespace="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ContactTypes"
    xmlns="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ContactTypes"
    elementFormDefault="qualified"
    xmlns:xsd="http://www.w3.org/2001/XMLSchema" >

    <xsd:complexType name="specialInstructionsType" mixed="true">
        <xsd:sequence>
            <xsd:any processContents="strict"
                namespace = "##targetNamespace"
                minOccurs="0" maxOccurs="unbounded" />
        </xsd:sequence>
    </xsd:complexType>

    <xsd:complexType name="phoneNumberType">
        <xsd:sequence>
            <xsd:element name="number" >
                <xsd:simpleType>
                    <xsd:restriction base="xsd:string">
                        <xsd:pattern value="[0-9\(\)\-]*"/>
                    </xsd:restriction>
                </xsd:simpleType>
            </xsd:element>
            <xsd:element name="SpecialInstructions" minOccurs="0" type="specialInstructionsType" />
        </xsd:sequence>
    </xsd:complexType>

    <xsd:complexType name="eMailType">
        <xsd:sequence>
            <xsd:element name="eMailAddress" type="xsd:string" />
            <xsd:element name="SpecialInstructions" minOccurs="0" type="specialInstructionsType" />
        </xsd:sequence>
    </xsd:complexType>

    <xsd:complexType name="addressType">
        <xsd:sequence>
            <xsd:element name="Street" type="xsd:string" minOccurs="1" maxOccurs="2" />
            <xsd:element name="City" type="xsd:string" minOccurs="1" maxOccurs="1" />
            <xsd:element name="StateProvince" type="xsd:string" minOccurs="1" maxOccurs="1" />
            <xsd:element name="PostalCode" type="xsd:string" minOccurs="0" maxOccurs="1" />
            <xsd:element name="CountryRegion" type="xsd:string" minOccurs="1" maxOccurs="1" />
            <xsd:element name="SpecialInstructions" type="specialInstructionsType" minOccurs="0"/>
        </xsd:sequence>
    </xsd:complexType>

    <xsd:element name="telephoneNumber"            type="phoneNumberType" />
    <xsd:element name="mobile"                     type="phoneNumberType" />
    <xsd:element name="pager"                      type="phoneNumberType" />
    <xsd:element name="facsimileTelephoneNumber"   type="phoneNumberType" />
    <xsd:element name="telexNumber"                type="phoneNumberType" />
    <xsd:element name="internationaliSDNNumber"    type="phoneNumberType" />
    <xsd:element name="eMail"                      type="eMailType" />
    <xsd:element name="homePostalAddress"          type="addressType" />
    <xsd:element name="physicalDeliveryOfficeName" type="addressType" />
    <xsd:element name="registeredAddress"          type="addressType" />
</xsd:schema>';
GO

-- Create Individual survey schema.
PRINT '';
PRINT 'Create Individual survey schema';
GO

CREATE XML SCHEMA COLLECTION [Person].[IndividualSurveySchemaCollection] AS
'<?xml version="1.0" encoding="UTF-8"?>
<xsd:schema targetNamespace="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/IndividualSurvey"
    xmlns="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/IndividualSurvey"
    elementFormDefault="qualified"
    xmlns:xsd="http://www.w3.org/2001/XMLSchema" >

    <xsd:simpleType name="SalaryType">
        <xsd:restriction base="xsd:string">
            <xsd:enumeration value="0-25000" />
            <xsd:enumeration value="25001-50000" />
            <xsd:enumeration value="50001-75000" />
            <xsd:enumeration value="75001-100000" />
            <xsd:enumeration value="greater than 100000" />
        </xsd:restriction>
    </xsd:simpleType>

    <xsd:simpleType name="MileRangeType">
        <xsd:restriction base="xsd:string">
            <xsd:enumeration value="0-1 Miles" />
            <xsd:enumeration value="1-2 Miles" />
            <xsd:enumeration value="2-5 Miles" />
            <xsd:enumeration value="5-10 Miles" />
            <xsd:enumeration value="10+ Miles" />
        </xsd:restriction>
    </xsd:simpleType>

    <xsd:element name="IndividualSurvey">
        <xsd:complexType>
            <xsd:sequence>
                <xsd:element name="TotalPurchaseYTD" type="xsd:decimal" minOccurs="0" maxOccurs="1" />
                <xsd:element name="DateFirstPurchase" type="xsd:date" minOccurs="0" maxOccurs="1" />
                <xsd:element name="BirthDate" type="xsd:date" minOccurs="0" maxOccurs="1" />
                <xsd:element name="MaritalStatus" type="xsd:string" minOccurs="0" maxOccurs="1" />
                <xsd:element name="YearlyIncome" type="SalaryType" minOccurs="0" maxOccurs="1" />
                <xsd:element name="Gender" type="xsd:string" minOccurs="0" maxOccurs="1" />
                <xsd:element name="TotalChildren" type="xsd:int" minOccurs="0" maxOccurs="1" />
                <xsd:element name="NumberChildrenAtHome" type="xsd:int" minOccurs="0" maxOccurs="1" />
                <xsd:element name="Education" type="xsd:string" minOccurs="0" maxOccurs="1" />
                <xsd:element name="Occupation" type="xsd:string" minOccurs="0" maxOccurs="1" />
                <xsd:element name="HomeOwnerFlag" type="xsd:string" minOccurs="0" maxOccurs="1" />
                <xsd:element name="NumberCarsOwned" type="xsd:int" minOccurs="0" maxOccurs="1" />
                <xsd:element name="Hobby" type="xsd:string" minOccurs="0" maxOccurs="unbounded" />
                <xsd:element name="CommuteDistance" type="MileRangeType" minOccurs="0" maxOccurs="1" />
                <xsd:element name="Comments" type="xsd:string" minOccurs="0" maxOccurs="1" />
            </xsd:sequence>
        </xsd:complexType>
    </xsd:element>
</xsd:schema>';
GO

-- Create resume schema.
PRINT '';
PRINT 'Create Resume schema';
GO

CREATE XML SCHEMA COLLECTION [HumanResources].[HRResumeSchemaCollection] AS
'<?xml version="1.0" encoding="UTF-8"?>
<xsd:schema targetNamespace="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/Resume"
    xmlns="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/Resume"
    xmlns:xsd="http://www.w3.org/2001/XMLSchema"
    elementFormDefault="qualified" >

    <xsd:element name="Resume" type="ResumeType"/>
    <xsd:element name="Address" type="AddressType"/>
    <xsd:element name="Education" type="EducationType"/>
    <xsd:element name="Employment" type="EmploymentType"/>
    <xsd:element name="Location" type="LocationType"/>
    <xsd:element name="Name" type="NameType"/>
    <xsd:element name="Telephone" type="TelephoneType"/>

    <xsd:complexType name="ResumeType">
        <xsd:sequence>
            <xsd:element ref="Name"/>
            <xsd:element name="Skills" type="xsd:string" minOccurs="0"/>
            <xsd:element ref="Employment" maxOccurs="unbounded"/>
            <xsd:element ref="Education" maxOccurs="unbounded"/>
            <xsd:element ref="Address" maxOccurs="unbounded"/>
            <xsd:element ref="Telephone" minOccurs="0"/>
            <xsd:element name="EMail" type="xsd:string" minOccurs="0"/>
            <xsd:element name="WebSite" type="xsd:string" minOccurs="0"/>
        </xsd:sequence>
    </xsd:complexType>

    <xsd:complexType name="AddressType">
        <xsd:sequence>
            <xsd:element name="Addr.Type" type="xsd:string">
                <xsd:annotation>
                    <xsd:documentation>Home|Work|Permanent</xsd:documentation>
                </xsd:annotation>
            </xsd:element>
            <xsd:element name="Addr.OrgName" type="xsd:string" minOccurs="0"/>
            <xsd:element name="Addr.Street" type="xsd:string" maxOccurs="unbounded"/>
            <xsd:element name="Addr.Location">
                <xsd:complexType>
                    <xsd:sequence>
                        <xsd:element ref="Location"/>
                    </xsd:sequence>
                </xsd:complexType>
            </xsd:element>
            <xsd:element name="Addr.PostalCode" type="xsd:string"/>
            <xsd:element name="Addr.Telephone" minOccurs="0">
                <xsd:complexType>
                    <xsd:sequence>
                        <xsd:element ref="Telephone" maxOccurs="unbounded"/>
                    </xsd:sequence>
                </xsd:complexType>
            </xsd:element>
        </xsd:sequence>
    </xsd:complexType>

    <xsd:complexType name="EducationType">
        <xsd:sequence>
            <xsd:element name="Edu.Level" type="xsd:string">
                <xsd:annotation>
                    <xsd:documentation>High School|Associate|Bachelor|Master|Doctorate</xsd:documentation>
                </xsd:annotation>
            </xsd:element>
            <xsd:element name="Edu.StartDate" type="xsd:date"/>
            <xsd:element name="Edu.EndDate" type="xsd:date"/>
            <xsd:element name="Edu.Degree" type="xsd:string" minOccurs="0"/>
            <xsd:element name="Edu.Major" type="xsd:string" minOccurs="0"/>
            <xsd:element name="Edu.Minor" type="xsd:string" minOccurs="0"/>
            <xsd:element name="Edu.GPA" type="xsd:string" minOccurs="0"/>
            <xsd:element name="Edu.GPAAlternate" type="xsd:decimal" minOccurs="0">
                <xsd:annotation>
                    <xsd:documentation>In case the institution does not follow a GPA system</xsd:documentation>
                </xsd:annotation>
            </xsd:element>
            <xsd:element name="Edu.GPAScale" type="xsd:decimal" minOccurs="0"/>
            <xsd:element name="Edu.School" type="xsd:string" minOccurs="0"/>
            <xsd:element name="Edu.Location" minOccurs="0">
                <xsd:complexType>
                    <xsd:sequence>
                        <xsd:element ref="Location"/>
                    </xsd:sequence>
                </xsd:complexType>
            </xsd:element>
        </xsd:sequence>
    </xsd:complexType>

    <xsd:complexType name="EmploymentType">
        <xsd:sequence>
            <xsd:element name="Emp.StartDate" type="xsd:date" minOccurs="0"/>
            <xsd:element name="Emp.EndDate" type="xsd:date" minOccurs="0"/>
            <xsd:element name="Emp.OrgName" type="xsd:string"/>
            <xsd:element name="Emp.JobTitle" type="xsd:string"/>
            <xsd:element name="Emp.Responsibility" type="xsd:string"/>
            <xsd:element name="Emp.FunctionCategory" type="xsd:string" minOccurs="0"/>
            <xsd:element name="Emp.IndustryCategory" type="xsd:string" minOccurs="0"/>
            <xsd:element name="Emp.Location" minOccurs="0">
                <xsd:complexType>
                    <xsd:sequence>
                        <xsd:element ref="Location"/>
                    </xsd:sequence>
                </xsd:complexType>
            </xsd:element>
        </xsd:sequence>
    </xsd:complexType>

    <xsd:complexType name="LocationType">
        <xsd:sequence>
            <xsd:element name="Loc.CountryRegion" type="xsd:string">
                <xsd:annotation>
                    <xsd:documentation>ISO 3166 Country Code</xsd:documentation>
                </xsd:annotation>
            </xsd:element>
            <xsd:element name="Loc.State" type="xsd:string" minOccurs="0"/>
            <xsd:element name="Loc.City" type="xsd:string" minOccurs="0"/>
        </xsd:sequence>
    </xsd:complexType>

    <xsd:complexType name="NameType">
        <xsd:sequence>
            <xsd:element name="Name.Prefix" type="xsd:string" minOccurs="0"/>
            <xsd:element name="Name.First" type="xsd:string"/>
            <xsd:element name="Name.Middle" type="xsd:string" minOccurs="0"/>
            <xsd:element name="Name.Last" type="xsd:string"/>
            <xsd:element name="Name.Suffix" type="xsd:string" minOccurs="0"/>
        </xsd:sequence>
    </xsd:complexType>

    <xsd:complexType name="TelephoneType">
        <xsd:sequence>
            <xsd:element name="Tel.Type" minOccurs="0">
                <xsd:annotation>
                    <xsd:documentation>Voice|Fax|Pager</xsd:documentation>
                </xsd:annotation>
            </xsd:element>
            <xsd:element name="Tel.IntlCode" type="xsd:int" minOccurs="0"/>
            <xsd:element name="Tel.AreaCode" type="xsd:int" minOccurs="0"/>
            <xsd:element name="Tel.Number" type="xsd:string"/>
            <xsd:element name="Tel.Extension" type="xsd:int" minOccurs="0"/>
        </xsd:sequence>
    </xsd:complexType>
</xsd:schema>';
GO

-- Create Product catalog description schema.
PRINT '';
PRINT 'Create Product catalog description schema';
GO

CREATE XML SCHEMA COLLECTION [Production].[ProductDescriptionSchemaCollection] AS
'<xsd:schema targetNamespace="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ProductModelWarrAndMain"
    xmlns="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ProductModelWarrAndMain"
    elementFormDefault="qualified"
    xmlns:xsd="http://www.w3.org/2001/XMLSchema" >
 
    <xsd:element name="Warranty"  >
        <xsd:complexType>
            <xsd:sequence>
                <xsd:element name="WarrantyPeriod" type="xsd:string"  />
                <xsd:element name="Description" type="xsd:string"  />
            </xsd:sequence>
        </xsd:complexType>
    </xsd:element>

    <xsd:element name="Maintenance"  >
        <xsd:complexType>
            <xsd:sequence>
                <xsd:element name="NoOfYears" type="xsd:string"  />
                <xsd:element name="Description" type="xsd:string"  />
            </xsd:sequence>
        </xsd:complexType>
    </xsd:element>
</xsd:schema>';

ALTER XML SCHEMA COLLECTION [Production].[ProductDescriptionSchemaCollection] ADD
'<?xml version="1.0" encoding="UTF-8"?>
<xs:schema targetNamespace="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ProductModelDescription"
    xmlns="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ProductModelDescription"
    elementFormDefault="qualified"
    xmlns:mstns="http://tempuri.org/XMLSchema.xsd"
    xmlns:xs="http://www.w3.org/2001/XMLSchema"
    xmlns:wm="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ProductModelWarrAndMain" >

    <xs:import
        namespace="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ProductModelWarrAndMain" />

    <xs:element name="ProductDescription" type="ProductDescription" />
        <xs:complexType name="ProductDescription">
            <xs:annotation>
                <xs:documentation>Product description has a summary blurb, if its manufactured elsewhere it
                includes a link to the manufacturers site for this component.
                Then it has optional zero or more sequences of features, pictures, categories
                and technical specifications.
                </xs:documentation>
            </xs:annotation>
            <xs:sequence>
                <xs:element name="Summary" type="Summary" minOccurs="0" />
                <xs:element name="Manufacturer" type="Manufacturer" minOccurs="0" />
                <xs:element name="Features" type="Features" minOccurs="0" maxOccurs="unbounded" />
                <xs:element name="Picture" type="Picture" minOccurs="0" maxOccurs="unbounded" />
                <xs:element name="Category" type="Category" minOccurs="0" maxOccurs="unbounded" />
                <xs:element name="Specifications" type="Specifications" minOccurs="0" maxOccurs="unbounded" />
            </xs:sequence>
            <xs:attribute name="ProductModelID" type="xs:string" />
            <xs:attribute name="ProductModelName" type="xs:string" />
        </xs:complexType>
 
        <xs:complexType name="Summary" mixed="true" >
            <xs:sequence>
                <xs:any processContents="skip" namespace="http://www.w3.org/1999/xhtml" minOccurs="0" maxOccurs="unbounded" />
            </xs:sequence>
        </xs:complexType>
       
        <xs:complexType name="Manufacturer">
            <xs:sequence>
                <xs:element name="Name" type="xs:string" minOccurs="0" />
                <xs:element name="CopyrightURL" type="xs:string" minOccurs="0" />
                <xs:element name="Copyright" type="xs:string" minOccurs="0" />
                <xs:element name="ProductURL" type="xs:string" minOccurs="0" />
            </xs:sequence>
        </xs:complexType>
 
        <xs:complexType name="Picture">
            <xs:annotation>
                <xs:documentation>Pictures of the component, some standard sizes are "Large" for zoom in, "Small" for a normal web page and "Thumbnail" for product listing pages.</xs:documentation>
            </xs:annotation>
            <xs:sequence>
                <xs:element name="Name" type="xs:string" minOccurs="0" />
                <xs:element name="Angle" type="xs:string" minOccurs="0" />
                <xs:element name="Size" type="xs:string" minOccurs="0" />
                <xs:element name="ProductPhotoID" type="xs:integer" minOccurs="0" />
            </xs:sequence>
        </xs:complexType>

        <xs:annotation>
            <xs:documentation>Features of the component that are more "sales" oriented.</xs:documentation>
        </xs:annotation>

        <xs:complexType name="Features" mixed="true"  >
            <xs:sequence>
                <xs:element ref="wm:Warranty"  />
                <xs:element ref="wm:Maintenance"  />
                <xs:any processContents="skip"  namespace="##other" minOccurs="0" maxOccurs="unbounded" />
            </xs:sequence>
        </xs:complexType>

        <xs:complexType name="Specifications" mixed="true">
            <xs:annotation>
                <xs:documentation>A single technical aspect of the component.</xs:documentation>
            </xs:annotation>
            <xs:sequence>
                <xs:any processContents="skip" minOccurs="0" maxOccurs="unbounded" />
            </xs:sequence>
        </xs:complexType>

        <xs:complexType name="Category">
            <xs:annotation>
                <xs:documentation>A single categorization element that designates a classification taxonomy and a code within that classification type.  Optional description for default display if needed.</xs:documentation>
            </xs:annotation>
            <xs:sequence>
                <xs:element ref="Taxonomy" />
                <xs:element ref="Code" />
                <xs:element ref="Description" minOccurs="0" />
            </xs:sequence>
        </xs:complexType>

    <xs:element name="Taxonomy" type="xs:string" />
    <xs:element name="Code" type="xs:string" />
    <xs:element name="Description" type="xs:string" />
</xs:schema>';
GO

-- Create Manufacturing instructions schema.
PRINT '';
PRINT 'Create Manufacturing instructions schema';
GO

CREATE XML SCHEMA COLLECTION [Production].[ManuInstructionsSchemaCollection] AS
'<?xml version="1.0" encoding="UTF-8"?>
<xsd:schema targetNamespace="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ProductModelManuInstructions"
    xmlns="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/ProductModelManuInstructions"
    elementFormDefault="qualified" attributeFormDefault="unqualified"
    xmlns:xsd="http://www.w3.org/2001/XMLSchema" >

    <xsd:annotation>
        <xsd:documentation>
            SetupHour   is the time it takes to set up the machine.
            MachineHour is the time the machine is busy manufcturing
            LaborHour   is the labor hours in the manu process
            LotSize     is the minimum quanity manufactured. For example,
                    no. of frames cut from the sheet metal
        </xsd:documentation>
    </xsd:annotation>

    <xsd:complexType name="StepType" mixed="true" >
        <xsd:choice  minOccurs="0" maxOccurs="unbounded" >
            <xsd:element name="tool" type="xsd:string" />
            <xsd:element name="material" type="xsd:string" />
            <xsd:element name="blueprint" type="xsd:string" />
            <xsd:element name="specs" type="xsd:string" />
            <xsd:element name="diag" type="xsd:string" />
        </xsd:choice>
    </xsd:complexType>

    <xsd:element  name="root">
        <xsd:complexType mixed="true">
            <xsd:sequence>
                <xsd:element name="Location" minOccurs="1" maxOccurs="unbounded">
                    <xsd:complexType mixed="true">
                        <xsd:sequence>
                            <xsd:element name="step" type="StepType" minOccurs="1" maxOccurs="unbounded" />
                        </xsd:sequence>
                        <xsd:attribute name="LocationID" type="xsd:integer" use="required"/>
                        <xsd:attribute name="SetupHours" type="xsd:decimal" use="optional"/>
                        <xsd:attribute name="MachineHours" type="xsd:decimal" use="optional"/>
                        <xsd:attribute name="LaborHours" type="xsd:decimal" use="optional"/>
                        <xsd:attribute name="LotSize" type="xsd:decimal" use="optional"/>
                    </xsd:complexType>
                </xsd:element>
            </xsd:sequence>
        </xsd:complexType>
    </xsd:element>
</xsd:schema>';
GO

-- Create Store survey schema.
PRINT '';
PRINT 'Create Store survey schema';
GO

CREATE XML SCHEMA COLLECTION [Sales].[StoreSurveySchemaCollection] AS
'<?xml version="1.0" encoding="UTF-8"?>
<xsd:schema xmlns:xsd="http://www.w3.org/2001/XMLSchema"
    targetNamespace="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/StoreSurvey"
    xmlns="http://schemas.microsoft.com/sqlserver/2004/07/adventure-works/StoreSurvey"
    elementFormDefault="qualified" attributeFormDefault="unqualified">

    <!-- BM=Bicycle manu BS=bicyle store OS=online store SGS=sporting goods store D=Discount Store -->
    <xsd:simpleType name="BusinessType">
        <xsd:restriction base="xsd:string">
            <xsd:enumeration value="BM" />
            <xsd:enumeration value="BS" />
            <xsd:enumeration value="D" />
            <xsd:enumeration value="OS" />
            <xsd:enumeration value="SGS" />
        </xsd:restriction>
    </xsd:simpleType>

    <!-- BMX=BMX Racing -->
    <xsd:simpleType name="SpecialtyType">
        <xsd:restriction base="xsd:string">
            <xsd:enumeration value="Family" />
            <xsd:enumeration value="Kids" />
            <xsd:enumeration value="BMX" />
            <xsd:enumeration value="Touring" />
            <xsd:enumeration value="Road" />
            <xsd:enumeration value="Mountain" />
            <xsd:enumeration value="All" />
        </xsd:restriction>
    </xsd:simpleType>

    <!-- AW=AdventureWorks only 2= AdvWorks+1 other brand other brand -->
    <xsd:simpleType name="BrandType">
        <xsd:restriction base="xsd:string">
            <xsd:enumeration value="AW" />
            <xsd:enumeration value="2" />
            <xsd:enumeration value="3" />
            <xsd:enumeration value="4+" />
        </xsd:restriction>
    </xsd:simpleType>

    <xsd:simpleType name="InternetType">
        <xsd:restriction base="xsd:string">
            <xsd:enumeration value="56kb" />
            <xsd:enumeration value="ISDN" />
            <xsd:enumeration value="DSL" />
            <xsd:enumeration value="T1" />
            <xsd:enumeration value="T2" />
            <xsd:enumeration value="T3" />
        </xsd:restriction>
    </xsd:simpleType>

    <xsd:element name="StoreSurvey">
        <xsd:complexType>
            <xsd:sequence>
                <xsd:element name="ContactName" type="xsd:string" minOccurs="0" maxOccurs="1" />
                <xsd:element name="JobTitle" type="xsd:string" minOccurs="0" maxOccurs="1" />
                <xsd:element name="AnnualSales" type="xsd:decimal" minOccurs="0" maxOccurs="1" />
                <xsd:element name="AnnualRevenue" type="xsd:decimal" minOccurs="0" maxOccurs="1" />
                <xsd:element name="BankName" type="xsd:string" minOccurs="0" maxOccurs="1" />
                <xsd:element name="BusinessType" type="BusinessType" minOccurs="0" maxOccurs="1" />
                <xsd:element name="YearOpened" type="xsd:gYear" minOccurs="0" maxOccurs="1" />
                <xsd:element name="Specialty" type="SpecialtyType" minOccurs="0" maxOccurs="1" />
                <xsd:element name="SquareFeet" type="xsd:float" minOccurs="0" maxOccurs="1" />
                <xsd:element name="Brands" type="BrandType" minOccurs="0" maxOccurs="1" />
                <xsd:element name="Internet" type="InternetType" minOccurs="0" maxOccurs="1" />
                <xsd:element name="NumberEmployees" type="xsd:int" minOccurs="0" maxOccurs="1" />
                <xsd:element name="Comments" type="xsd:string" minOccurs="0" maxOccurs="1" />
            </xsd:sequence>
        </xsd:complexType>
    </xsd:element>
</xsd:schema>';
GO

-- ******************************************************
-- Create tables
-- ******************************************************
PRINT '';
PRINT '*** Creating Tables';
GO

CREATE TABLE [Person].[Address](
    [AddressID] [int] IDENTITY (1, 1) NOT FOR REPLICATION NOT NULL,
    [AddressLine1] [nvarchar](60) NOT NULL,
    [AddressLine2] [nvarchar](60) NULL,
    [City] [nvarchar](30) NOT NULL,
    [StateProvinceID] [int] NOT NULL,
    [PostalCode] [nvarchar](15) NOT NULL,
	[SpatialLocation] [geography] NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_Address_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_Address_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Person].[AddressType](
    [AddressTypeID] [int] IDENTITY (1, 1) NOT NULL,
    [Name] [Name] NOT NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_AddressType_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_AddressType_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [dbo].[AWBuildVersion](
    [SystemInformationID] [tinyint] IDENTITY (1, 1) NOT NULL,
    [Database Version] [nvarchar](25) NOT NULL,
    [VersionDate] [datetime] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_AWBuildVersion_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Production].[BillOfMaterials](
    [BillOfMaterialsID] [int] IDENTITY (1, 1) NOT NULL,
    [ProductAssemblyID] [int] NULL,
    [ComponentID] [int] NOT NULL,
    [StartDate] [datetime] NOT NULL CONSTRAINT [DF_BillOfMaterials_StartDate] DEFAULT (GETDATE()),
    [EndDate] [datetime] NULL,
    [UnitMeasureCode] [nchar](3) NOT NULL,
    [BOMLevel] [smallint] NOT NULL,
    [PerAssemblyQty] [decimal](8, 2) NOT NULL CONSTRAINT [DF_BillOfMaterials_PerAssemblyQty] DEFAULT (1.00),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_BillOfMaterials_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_BillOfMaterials_EndDate] CHECK (([EndDate] > [StartDate]) OR ([EndDate] IS NULL)),
    CONSTRAINT [CK_BillOfMaterials_ProductAssemblyID] CHECK ([ProductAssemblyID] <> [ComponentID]),
    CONSTRAINT [CK_BillOfMaterials_BOMLevel] CHECK ((([ProductAssemblyID] IS NULL)
        AND ([BOMLevel] = 0) AND ([PerAssemblyQty] = 1.00))
        OR (([ProductAssemblyID] IS NOT NULL) AND ([BOMLevel] >= 1))),
    CONSTRAINT [CK_BillOfMaterials_PerAssemblyQty] CHECK ([PerAssemblyQty] >= 1.00)
) ON [PRIMARY];
GO

CREATE TABLE [Person].[BusinessEntity](
	[BusinessEntityID] [int] IDENTITY (1, 1) NOT FOR REPLICATION NOT NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_BusinessEntity_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_BusinessEntity_ModifiedDate] DEFAULT (GETDATE())	
) ON [PRIMARY];
GO

CREATE TABLE [Person].[BusinessEntityAddress](
	[BusinessEntityID] [int] NOT NULL,
    [AddressID] [int] NOT NULL,
    [AddressTypeID] [int] NOT NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_BusinessEntityAddress_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_BusinessEntityAddress_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];

CREATE TABLE [Person].[BusinessEntityContact](
	[BusinessEntityID] [int] NOT NULL,
    [PersonID] [int] NOT NULL,
    [ContactTypeID] [int] NOT NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_BusinessEntityContact_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_BusinessEntityContact_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Person].[ContactType](
    [ContactTypeID] [int] IDENTITY (1, 1) NOT NULL,
    [Name] [Name] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ContactType_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[CountryRegionCurrency](
    [CountryRegionCode] [nvarchar](3) NOT NULL,
    [CurrencyCode] [nchar](3) NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_CountryRegionCurrency_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Person].[CountryRegion](
    [CountryRegionCode] [nvarchar](3) NOT NULL,
    [Name] [Name] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_CountryRegion_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[CreditCard](
    [CreditCardID] [int] IDENTITY (1, 1) NOT NULL,
    [CardType] [nvarchar](50) NOT NULL,
    [CardNumber] [nvarchar](25) NOT NULL,
    [ExpMonth] [tinyint] NOT NULL,
    [ExpYear] [smallint] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_CreditCard_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Production].[Culture](
    [CultureID] [nchar](6) NOT NULL,
    [Name] [Name] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_Culture_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[Currency](
    [CurrencyCode] [nchar](3) NOT NULL,
    [Name] [Name] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_Currency_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[CurrencyRate](
    [CurrencyRateID] [int] IDENTITY (1, 1) NOT NULL,
    [CurrencyRateDate] [datetime] NOT NULL,   
    [FromCurrencyCode] [nchar](3) NOT NULL,
    [ToCurrencyCode] [nchar](3) NOT NULL,
    [AverageRate] [money] NOT NULL,
    [EndOfDayRate] [money] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_CurrencyRate_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[Customer](
	[CustomerID] [int] IDENTITY (1, 1) NOT FOR REPLICATION NOT NULL,
	-- A customer may either be a person, a store, or a person who works for a store
	[PersonID] [int] NULL, -- If this customer represents a person, this is non-null
    [StoreID] [int] NULL,  -- If the customer is a store, or is associated with a store then this is non-null.
    [TerritoryID] [int] NULL,
    [AccountNumber] AS ISNULL('AW' + [dbo].[ufnLeadingZeros](CustomerID), ''),
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_Customer_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_Customer_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [HumanResources].[Department](
    [DepartmentID] [smallint] IDENTITY (1, 1) NOT NULL,
    [Name] [Name] NOT NULL,
    [GroupName] [Name] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_Department_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Production].[Document](
    [DocumentNode] [hierarchyid] NOT NULL,
	[DocumentLevel] AS DocumentNode.GetLevel(),
    [Title] [nvarchar](50) NOT NULL,
	[Owner] [int] NOT NULL,
	[FolderFlag] [bit] NOT NULL CONSTRAINT [DF_Document_FolderFlag] DEFAULT (0),
    [FileName] [nvarchar](400) NOT NULL,
    [FileExtension] nvarchar(8) NOT NULL,
    [Revision] [nchar](5) NOT NULL,
    [ChangeNumber] [int] NOT NULL CONSTRAINT [DF_Document_ChangeNumber] DEFAULT (0),
    [Status] [tinyint] NOT NULL,
    [DocumentSummary] [nvarchar](max) NULL,
    [Document] [varbinary](max)  NULL, 
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL UNIQUE CONSTRAINT [DF_Document_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_Document_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_Document_Status] CHECK ([Status] BETWEEN 1 AND 3)
) ON [PRIMARY];
GO

CREATE TABLE [Person].[EmailAddress](
	[BusinessEntityID] [int] NOT NULL,
	[EmailAddressID] [int] IDENTITY (1, 1) NOT NULL,
    [EmailAddress] [nvarchar](50) NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_EmailAddress_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_EmailAddress_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO
CREATE TABLE [HumanResources].[Employee](
    [BusinessEntityID] [int] NOT NULL,
    [NationalIDNumber] [nvarchar](15) NOT NULL,
    [LoginID] [nvarchar](256) NOT NULL,    
    [OrganizationNode] [hierarchyid] NULL,
	[OrganizationLevel] AS OrganizationNode.GetLevel(),
    [JobTitle] [nvarchar](50) NOT NULL,
    [BirthDate] [date] NOT NULL,
    [MaritalStatus] [nchar](1) NOT NULL,
    [Gender] [nchar](1) NOT NULL,
    [HireDate] [date] NOT NULL,
    [SalariedFlag] [Flag] NOT NULL CONSTRAINT [DF_Employee_SalariedFlag] DEFAULT (1),
    [VacationHours] [smallint] NOT NULL CONSTRAINT [DF_Employee_VacationHours] DEFAULT (0),
    [SickLeaveHours] [smallint] NOT NULL CONSTRAINT [DF_Employee_SickLeaveHours] DEFAULT (0),
    [CurrentFlag] [Flag] NOT NULL CONSTRAINT [DF_Employee_CurrentFlag] DEFAULT (1),
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_Employee_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_Employee_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_Employee_BirthDate] CHECK ([BirthDate] BETWEEN '1930-01-01' AND DATEADD(YEAR, -18, GETDATE())),
    CONSTRAINT [CK_Employee_MaritalStatus] CHECK (UPPER([MaritalStatus]) IN ('M', 'S')), -- Married or Single
    CONSTRAINT [CK_Employee_HireDate] CHECK ([HireDate] BETWEEN '1996-07-01' AND DATEADD(DAY, 1, GETDATE())),
    CONSTRAINT [CK_Employee_Gender] CHECK (UPPER([Gender]) IN ('M', 'F')), -- Male or Female
    CONSTRAINT [CK_Employee_VacationHours] CHECK ([VacationHours] BETWEEN -40 AND 240),
    CONSTRAINT [CK_Employee_SickLeaveHours] CHECK ([SickLeaveHours] BETWEEN 0 AND 120)
) ON [PRIMARY];
GO

CREATE TABLE [HumanResources].[EmployeeDepartmentHistory](
    [BusinessEntityID] [int] NOT NULL,
    [DepartmentID] [smallint] NOT NULL,
    [ShiftID] [tinyint] NOT NULL,
    [StartDate] [date] NOT NULL,
    [EndDate] [date] NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_EmployeeDepartmentHistory_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_EmployeeDepartmentHistory_EndDate] CHECK (([EndDate] >= [StartDate]) OR ([EndDate] IS NULL)),
) ON [PRIMARY];
GO

CREATE TABLE [HumanResources].[EmployeePayHistory](
    [BusinessEntityID] [int] NOT NULL,
    [RateChangeDate] [datetime] NOT NULL,
    [Rate] [money] NOT NULL,
    [PayFrequency] [tinyint] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_EmployeePayHistory_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_EmployeePayHistory_PayFrequency] CHECK ([PayFrequency] IN (1, 2)), -- 1 = monthly salary, 2 = biweekly salary
    CONSTRAINT [CK_EmployeePayHistory_Rate] CHECK ([Rate] BETWEEN 6.50 AND 200.00)
) ON [PRIMARY];
GO

CREATE TABLE [Production].[Illustration](
    [IllustrationID] [int] IDENTITY (1, 1) NOT NULL,
    [Diagram] [XML] NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_Illustration_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [HumanResources].[JobCandidate](
    [JobCandidateID] [int] IDENTITY (1, 1) NOT NULL,
    [BusinessEntityID] [int] NULL,
    [Resume] [XML]([HumanResources].[HRResumeSchemaCollection]) NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_JobCandidate_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Production].[Location](
    [LocationID] [smallint] IDENTITY (1, 1) NOT NULL,
    [Name] [Name] NOT NULL,
    [CostRate] [smallmoney] NOT NULL CONSTRAINT [DF_Location_CostRate] DEFAULT (0.00),
    [Availability] [decimal](8, 2) NOT NULL CONSTRAINT [DF_Location_Availability] DEFAULT (0.00),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_Location_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_Location_CostRate] CHECK ([CostRate] >= 0.00),
    CONSTRAINT [CK_Location_Availability] CHECK ([Availability] >= 0.00)
) ON [PRIMARY];
GO

CREATE TABLE [Person].[Password](
	[BusinessEntityID] [int] NOT NULL,
    [PasswordHash] [varchar](128) NOT NULL,
    [PasswordSalt] [varchar](10) NOT NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_Password_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_Password_ModifiedDate] DEFAULT (GETDATE())

) ON [PRIMARY];
GO

CREATE TABLE [Person].[Person](
    [BusinessEntityID] [int] NOT NULL,
	[PersonType] [nchar](2) NOT NULL,
    [NameStyle] [NameStyle] NOT NULL CONSTRAINT [DF_Person_NameStyle] DEFAULT (0),
    [Title] [nvarchar](8) NULL,
    [FirstName] [Name] NOT NULL,
    [MiddleName] [Name] NULL,
    [LastName] [Name] NOT NULL,
    [Suffix] [nvarchar](10) NULL,
    [EmailPromotion] [int] NOT NULL CONSTRAINT [DF_Person_EmailPromotion] DEFAULT (0),
    [AdditionalContactInfo] [XML]([Person].[AdditionalContactInfoSchemaCollection]) NULL,
    [Demographics] [XML]([Person].[IndividualSurveySchemaCollection]) NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_Person_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_Person_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_Person_EmailPromotion] CHECK ([EmailPromotion] BETWEEN 0 AND 2),
    CONSTRAINT [CK_Person_PersonType] CHECK ([PersonType] IS NULL OR UPPER([PersonType]) IN ('SC', 'VC', 'IN', 'EM', 'SP', 'GC'))
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[PersonCreditCard](
    [BusinessEntityID] [int] NOT NULL,
    [CreditCardID] [int] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_PersonCreditCard_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Person].[PersonPhone](
    [BusinessEntityID] [int] NOT NULL,
	[PhoneNumber] [Phone] NOT NULL,
	[PhoneNumberTypeID] [int] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_PersonPhone_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Person].[PhoneNumberType](
	[PhoneNumberTypeID] [int] IDENTITY (1, 1) NOT NULL,
	[Name] [Name] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_PhoneNumberType_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Production].[Product](
    [ProductID] [int] IDENTITY (1, 1) NOT NULL,
    [Name] [Name] NOT NULL,
    [ProductNumber] [nvarchar](25) NOT NULL,
    [MakeFlag] [Flag] NOT NULL CONSTRAINT [DF_Product_MakeFlag] DEFAULT (1),
    [FinishedGoodsFlag] [Flag] NOT NULL CONSTRAINT [DF_Product_FinishedGoodsFlag] DEFAULT (1),
    [Color] [nvarchar](15) NULL,
    [SafetyStockLevel] [smallint] NOT NULL,
    [ReorderPoint] [smallint] NOT NULL,
    [StandardCost] [money] NOT NULL,
    [ListPrice] [money] NOT NULL,
    [Size] [nvarchar](5) NULL,
    [SizeUnitMeasureCode] [nchar](3) NULL,
    [WeightUnitMeasureCode] [nchar](3) NULL,
    [Weight] [decimal](8, 2) NULL,
    [DaysToManufacture] [int] NOT NULL,
    [ProductLine] [nchar](2) NULL,
    [Class] [nchar](2) NULL,
    [Style] [nchar](2) NULL,
    [ProductSubcategoryID] [int] NULL,
    [ProductModelID] [int] NULL,
    [SellStartDate] [datetime] NOT NULL,
    [SellEndDate] [datetime] NULL,
    [DiscontinuedDate] [datetime] NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_Product_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_Product_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_Product_SafetyStockLevel] CHECK ([SafetyStockLevel] > 0),
    CONSTRAINT [CK_Product_ReorderPoint] CHECK ([ReorderPoint] > 0),
    CONSTRAINT [CK_Product_StandardCost] CHECK ([StandardCost] >= 0.00),
    CONSTRAINT [CK_Product_ListPrice] CHECK ([ListPrice] >= 0.00),
    CONSTRAINT [CK_Product_Weight] CHECK ([Weight] > 0.00),
    CONSTRAINT [CK_Product_DaysToManufacture] CHECK ([DaysToManufacture] >= 0),
    CONSTRAINT [CK_Product_ProductLine] CHECK (UPPER([ProductLine]) IN ('S', 'T', 'M', 'R') OR [ProductLine] IS NULL),
    CONSTRAINT [CK_Product_Class] CHECK (UPPER([Class]) IN ('L', 'M', 'H') OR [Class] IS NULL),
    CONSTRAINT [CK_Product_Style] CHECK (UPPER([Style]) IN ('W', 'M', 'U') OR [Style] IS NULL),
    CONSTRAINT [CK_Product_SellEndDate] CHECK (([SellEndDate] >= [SellStartDate]) OR ([SellEndDate] IS NULL)),
) ON [PRIMARY];
GO

CREATE TABLE [Production].[ProductCategory](
    [ProductCategoryID] [int] IDENTITY (1, 1) NOT NULL,
    [Name] [Name] NOT NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_ProductCategory_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ProductCategory_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Production].[ProductCostHistory](
    [ProductID] [int] NOT NULL,
    [StartDate] [datetime] NOT NULL,
    [EndDate] [datetime] NULL,
    [StandardCost] [money] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ProductCostHistory_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_ProductCostHistory_EndDate] CHECK (([EndDate] >= [StartDate]) OR ([EndDate] IS NULL)),
    CONSTRAINT [CK_ProductCostHistory_StandardCost] CHECK ([StandardCost] >= 0.00)
) ON [PRIMARY];
GO

CREATE TABLE [Production].[ProductDescription](
    [ProductDescriptionID] [int] IDENTITY (1, 1) NOT NULL,
    [Description] [nvarchar](400) NOT NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_ProductDescription_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ProductDescription_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Production].[ProductDocument](
    [ProductID] [int] NOT NULL,
    [DocumentNode] [hierarchyid] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ProductDocument_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Production].[ProductInventory](
    [ProductID] [int] NOT NULL,
    [LocationID] [smallint] NOT NULL,
    [Shelf] [nvarchar](10) NOT NULL,
    [Bin] [tinyint] NOT NULL,
    [Quantity] [smallint] NOT NULL CONSTRAINT [DF_ProductInventory_Quantity] DEFAULT (0),
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_ProductInventory_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ProductInventory_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_ProductInventory_Shelf] CHECK (([Shelf] LIKE '[A-Za-z]') OR ([Shelf] = 'N/A')),
    CONSTRAINT [CK_ProductInventory_Bin] CHECK ([Bin] BETWEEN 0 AND 100)
) ON [PRIMARY];
GO

CREATE TABLE [Production].[ProductListPriceHistory](
    [ProductID] [int] NOT NULL,
    [StartDate] [datetime] NOT NULL,
    [EndDate] [datetime] NULL,
    [ListPrice] [money] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ProductListPriceHistory_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_ProductListPriceHistory_EndDate] CHECK (([EndDate] >= [StartDate]) OR ([EndDate] IS NULL)),
    CONSTRAINT [CK_ProductListPriceHistory_ListPrice] CHECK ([ListPrice] > 0.00)
) ON [PRIMARY];
GO

CREATE TABLE [Production].[ProductModel](
    [ProductModelID] [int] IDENTITY (1, 1) NOT NULL,
    [Name] [Name] NOT NULL,
    [CatalogDescription] [XML]([Production].[ProductDescriptionSchemaCollection]) NULL,
    [Instructions] [XML]([Production].[ManuInstructionsSchemaCollection]) NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_ProductModel_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ProductModel_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Production].[ProductModelIllustration](
    [ProductModelID] [int] NOT NULL,
    [IllustrationID] [int] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ProductModelIllustration_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Production].[ProductModelProductDescriptionCulture](
    [ProductModelID] [int] NOT NULL,
    [ProductDescriptionID] [int] NOT NULL,
    [CultureID] [nchar](6) NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ProductModelProductDescriptionCulture_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Production].[ProductPhoto](
    [ProductPhotoID] [int] IDENTITY (1, 1) NOT NULL,
    [ThumbNailPhoto] [varbinary](max) NULL,
    [ThumbnailPhotoFileName] [nvarchar](50) NULL,
    [LargePhoto] [varbinary](max) NULL,
    [LargePhotoFileName] [nvarchar](50) NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ProductPhoto_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Production].[ProductProductPhoto](
    [ProductID] [int] NOT NULL,
    [ProductPhotoID] [int] NOT NULL,
    [Primary] [Flag] NOT NULL CONSTRAINT [DF_ProductProductPhoto_Primary] DEFAULT (0),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ProductProductPhoto_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Production].[ProductReview](
    [ProductReviewID] [int] IDENTITY (1, 1) NOT NULL,
    [ProductID] [int] NOT NULL,
    [ReviewerName] [Name] NOT NULL,
    [ReviewDate] [datetime] NOT NULL CONSTRAINT [DF_ProductReview_ReviewDate] DEFAULT (GETDATE()),
    [EmailAddress] [nvarchar](50) NOT NULL,
    [Rating] [int] NOT NULL,
    [Comments] [nvarchar](3850),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ProductReview_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_ProductReview_Rating] CHECK ([Rating] BETWEEN 1 AND 5),
) ON [PRIMARY];
GO

CREATE TABLE [Production].[ProductSubcategory](
    [ProductSubcategoryID] [int] IDENTITY (1, 1) NOT NULL,
    [ProductCategoryID] [int] NOT NULL,
    [Name] [Name] NOT NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_ProductSubcategory_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ProductSubcategory_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Purchasing].[ProductVendor](
    [ProductID] [int] NOT NULL,
    [BusinessEntityID] [int] NOT NULL,
    [AverageLeadTime] [int] NOT NULL,
    [StandardPrice] [money] NOT NULL,
    [LastReceiptCost] [money] NULL,
    [LastReceiptDate] [datetime] NULL,
    [MinOrderQty] [int] NOT NULL,
    [MaxOrderQty] [int] NOT NULL,
    [OnOrderQty] [int] NULL,
    [UnitMeasureCode] [nchar](3) NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ProductVendor_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_ProductVendor_AverageLeadTime] CHECK ([AverageLeadTime] >= 1),
    CONSTRAINT [CK_ProductVendor_StandardPrice] CHECK ([StandardPrice] > 0.00),
    CONSTRAINT [CK_ProductVendor_LastReceiptCost] CHECK ([LastReceiptCost] > 0.00),
    CONSTRAINT [CK_ProductVendor_MinOrderQty] CHECK ([MinOrderQty] >= 1),
    CONSTRAINT [CK_ProductVendor_MaxOrderQty] CHECK ([MaxOrderQty] >= 1),
    CONSTRAINT [CK_ProductVendor_OnOrderQty] CHECK ([OnOrderQty] >= 0)
) ON [PRIMARY];
GO

CREATE TABLE [Purchasing].[PurchaseOrderDetail](
    [PurchaseOrderID] [int] NOT NULL,
    [PurchaseOrderDetailID] [int] IDENTITY (1, 1) NOT NULL,
    [DueDate] [datetime] NOT NULL,
    [OrderQty] [smallint] NOT NULL,
    [ProductID] [int] NOT NULL,
    [UnitPrice] [money] NOT NULL,
    [LineTotal] AS ISNULL([OrderQty] * [UnitPrice], 0.00),
    [ReceivedQty] [decimal](8, 2) NOT NULL,
    [RejectedQty] [decimal](8, 2) NOT NULL,
    [StockedQty] AS ISNULL([ReceivedQty] - [RejectedQty], 0.00),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_PurchaseOrderDetail_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_PurchaseOrderDetail_OrderQty] CHECK ([OrderQty] > 0),
    CONSTRAINT [CK_PurchaseOrderDetail_UnitPrice] CHECK ([UnitPrice] >= 0.00),
    CONSTRAINT [CK_PurchaseOrderDetail_ReceivedQty] CHECK ([ReceivedQty] >= 0.00),
    CONSTRAINT [CK_PurchaseOrderDetail_RejectedQty] CHECK ([RejectedQty] >= 0.00)
) ON [PRIMARY];
GO

CREATE TABLE [Purchasing].[PurchaseOrderHeader](
    [PurchaseOrderID] [int] IDENTITY (1, 1) NOT NULL,
    [RevisionNumber] [tinyint] NOT NULL CONSTRAINT [DF_PurchaseOrderHeader_RevisionNumber] DEFAULT (0),
    [Status] [tinyint] NOT NULL CONSTRAINT [DF_PurchaseOrderHeader_Status] DEFAULT (1),
    [EmployeeID] [int] NOT NULL,
    [VendorID] [int] NOT NULL,
    [ShipMethodID] [int] NOT NULL,
    [OrderDate] [datetime] NOT NULL CONSTRAINT [DF_PurchaseOrderHeader_OrderDate] DEFAULT (GETDATE()),
    [ShipDate] [datetime] NULL,
    [SubTotal] [money] NOT NULL CONSTRAINT [DF_PurchaseOrderHeader_SubTotal] DEFAULT (0.00),
    [TaxAmt] [money] NOT NULL CONSTRAINT [DF_PurchaseOrderHeader_TaxAmt] DEFAULT (0.00),
    [Freight] [money] NOT NULL CONSTRAINT [DF_PurchaseOrderHeader_Freight] DEFAULT (0.00),
    [TotalDue] AS ISNULL([SubTotal] + [TaxAmt] + [Freight], 0) PERSISTED NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_PurchaseOrderHeader_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_PurchaseOrderHeader_Status] CHECK ([Status] BETWEEN 1 AND 4), -- 1 = Pending; 2 = Approved; 3 = Rejected; 4 = Complete
    CONSTRAINT [CK_PurchaseOrderHeader_ShipDate] CHECK (([ShipDate] >= [OrderDate]) OR ([ShipDate] IS NULL)),
    CONSTRAINT [CK_PurchaseOrderHeader_SubTotal] CHECK ([SubTotal] >= 0.00),
    CONSTRAINT [CK_PurchaseOrderHeader_TaxAmt] CHECK ([TaxAmt] >= 0.00),
    CONSTRAINT [CK_PurchaseOrderHeader_Freight] CHECK ([Freight] >= 0.00)
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[SalesOrderDetail](
    [SalesOrderID] [int] NOT NULL,
    [SalesOrderDetailID] [int] IDENTITY (1, 1) NOT NULL,
    [CarrierTrackingNumber] [nvarchar](25) NULL,
    [OrderQty] [smallint] NOT NULL,
    [ProductID] [int] NOT NULL,
    [SpecialOfferID] [int] NOT NULL,
    [UnitPrice] [money] NOT NULL,
    [UnitPriceDiscount] [money] NOT NULL CONSTRAINT [DF_SalesOrderDetail_UnitPriceDiscount] DEFAULT (0.0),
    [LineTotal] AS ISNULL([UnitPrice] * (1.0 - [UnitPriceDiscount]) * [OrderQty], 0.0),
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_SalesOrderDetail_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_SalesOrderDetail_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_SalesOrderDetail_OrderQty] CHECK ([OrderQty] > 0),
    CONSTRAINT [CK_SalesOrderDetail_UnitPrice] CHECK ([UnitPrice] >= 0.00),
    CONSTRAINT [CK_SalesOrderDetail_UnitPriceDiscount] CHECK ([UnitPriceDiscount] >= 0.00)
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[SalesOrderHeader](
    [SalesOrderID] [int] IDENTITY (1, 1) NOT FOR REPLICATION NOT NULL,
    [RevisionNumber] [tinyint] NOT NULL CONSTRAINT [DF_SalesOrderHeader_RevisionNumber] DEFAULT (0),
    [OrderDate] [datetime] NOT NULL CONSTRAINT [DF_SalesOrderHeader_OrderDate] DEFAULT (GETDATE()),
    [DueDate] [datetime] NOT NULL,
    [ShipDate] [datetime] NULL,
    [Status] [tinyint] NOT NULL CONSTRAINT [DF_SalesOrderHeader_Status] DEFAULT (1),
    [OnlineOrderFlag] [Flag] NOT NULL CONSTRAINT [DF_SalesOrderHeader_OnlineOrderFlag] DEFAULT (1),
    [SalesOrderNumber] AS ISNULL(N'SO' + CONVERT(nvarchar(23), [SalesOrderID]), N'*** ERROR ***'),
    [PurchaseOrderNumber] [OrderNumber] NULL,
    [AccountNumber] [AccountNumber] NULL,
    [CustomerID] [int] NOT NULL,
    [SalesPersonID] [int] NULL,
    [TerritoryID] [int] NULL,
    [BillToAddressID] [int] NOT NULL,
    [ShipToAddressID] [int] NOT NULL,
    [ShipMethodID] [int] NOT NULL,
    [CreditCardID] [int] NULL,
    [CreditCardApprovalCode] [varchar](15) NULL,   
    [CurrencyRateID] [int] NULL,
    [SubTotal] [money] NOT NULL CONSTRAINT [DF_SalesOrderHeader_SubTotal] DEFAULT (0.00),
    [TaxAmt] [money] NOT NULL CONSTRAINT [DF_SalesOrderHeader_TaxAmt] DEFAULT (0.00),
    [Freight] [money] NOT NULL CONSTRAINT [DF_SalesOrderHeader_Freight] DEFAULT (0.00),
    [TotalDue] AS ISNULL([SubTotal] + [TaxAmt] + [Freight], 0),
    [Comment] [nvarchar](128) NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_SalesOrderHeader_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_SalesOrderHeader_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_SalesOrderHeader_Status] CHECK ([Status] BETWEEN 0 AND 8),
    CONSTRAINT [CK_SalesOrderHeader_DueDate] CHECK ([DueDate] >= [OrderDate]),
    CONSTRAINT [CK_SalesOrderHeader_ShipDate] CHECK (([ShipDate] >= [OrderDate]) OR ([ShipDate] IS NULL)),
    CONSTRAINT [CK_SalesOrderHeader_SubTotal] CHECK ([SubTotal] >= 0.00),
    CONSTRAINT [CK_SalesOrderHeader_TaxAmt] CHECK ([TaxAmt] >= 0.00),
    CONSTRAINT [CK_SalesOrderHeader_Freight] CHECK ([Freight] >= 0.00)
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[SalesOrderHeaderSalesReason](
    [SalesOrderID] [int] NOT NULL,
    [SalesReasonID] [int] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_SalesOrderHeaderSalesReason_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[SalesPerson](
    [BusinessEntityID] [int] NOT NULL,
    [TerritoryID] [int] NULL,
    [SalesQuota] [money] NULL,
    [Bonus] [money] NOT NULL CONSTRAINT [DF_SalesPerson_Bonus] DEFAULT (0.00),
    [CommissionPct] [smallmoney] NOT NULL CONSTRAINT [DF_SalesPerson_CommissionPct] DEFAULT (0.00),
    [SalesYTD] [money] NOT NULL CONSTRAINT [DF_SalesPerson_SalesYTD] DEFAULT (0.00),
    [SalesLastYear] [money] NOT NULL CONSTRAINT [DF_SalesPerson_SalesLastYear] DEFAULT (0.00),
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_SalesPerson_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_SalesPerson_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_SalesPerson_SalesQuota] CHECK ([SalesQuota] > 0.00),
    CONSTRAINT [CK_SalesPerson_Bonus] CHECK ([Bonus] >= 0.00),
    CONSTRAINT [CK_SalesPerson_CommissionPct] CHECK ([CommissionPct] >= 0.00),
    CONSTRAINT [CK_SalesPerson_SalesYTD] CHECK ([SalesYTD] >= 0.00),
    CONSTRAINT [CK_SalesPerson_SalesLastYear] CHECK ([SalesLastYear] >= 0.00)
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[SalesPersonQuotaHistory](
    [BusinessEntityID] [int] NOT NULL,
    [QuotaDate] [datetime] NOT NULL,
    [SalesQuota] [money] NOT NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_SalesPersonQuotaHistory_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_SalesPersonQuotaHistory_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_SalesPersonQuotaHistory_SalesQuota] CHECK ([SalesQuota] > 0.00)
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[SalesReason](
    [SalesReasonID] [int] IDENTITY (1, 1) NOT NULL,
    [Name] [Name] NOT NULL,
    [ReasonType] [Name] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_SalesReason_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[SalesTaxRate](
    [SalesTaxRateID] [int] IDENTITY (1, 1) NOT NULL,
    [StateProvinceID] [int] NOT NULL,
    [TaxType] [tinyint] NOT NULL,
    [TaxRate] [smallmoney] NOT NULL CONSTRAINT [DF_SalesTaxRate_TaxRate] DEFAULT (0.00),
    [Name] [Name] NOT NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_SalesTaxRate_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_SalesTaxRate_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_SalesTaxRate_TaxType] CHECK ([TaxType] BETWEEN 1 AND 3)
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[SalesTerritory](
    [TerritoryID] [int] IDENTITY (1, 1) NOT NULL,
    [Name] [Name] NOT NULL,
    [CountryRegionCode] [nvarchar](3) NOT NULL,
    [Group] [nvarchar](50) NOT NULL,
    [SalesYTD] [money] NOT NULL CONSTRAINT [DF_SalesTerritory_SalesYTD] DEFAULT (0.00),
    [SalesLastYear] [money] NOT NULL CONSTRAINT [DF_SalesTerritory_SalesLastYear] DEFAULT (0.00),
    [CostYTD] [money] NOT NULL CONSTRAINT [DF_SalesTerritory_CostYTD] DEFAULT (0.00),
    [CostLastYear] [money] NOT NULL CONSTRAINT [DF_SalesTerritory_CostLastYear] DEFAULT (0.00),
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_SalesTerritory_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_SalesTerritory_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_SalesTerritory_SalesYTD] CHECK ([SalesYTD] >= 0.00),
    CONSTRAINT [CK_SalesTerritory_SalesLastYear] CHECK ([SalesLastYear] >= 0.00),
    CONSTRAINT [CK_SalesTerritory_CostYTD] CHECK ([CostYTD] >= 0.00),
    CONSTRAINT [CK_SalesTerritory_CostLastYear] CHECK ([CostLastYear] >= 0.00)
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[SalesTerritoryHistory](
    [BusinessEntityID] [int] NOT NULL,  -- A sales person
    [TerritoryID] [int] NOT NULL,
    [StartDate] [datetime] NOT NULL,
    [EndDate] [datetime] NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_SalesTerritoryHistory_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_SalesTerritoryHistory_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_SalesTerritoryHistory_EndDate] CHECK (([EndDate] >= [StartDate]) OR ([EndDate] IS NULL))
) ON [PRIMARY];
GO

CREATE TABLE [Production].[ScrapReason](
    [ScrapReasonID] [smallint] IDENTITY (1, 1) NOT NULL,
    [Name] [Name] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ScrapReason_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [HumanResources].[Shift](
    [ShiftID] [tinyint] IDENTITY (1, 1) NOT NULL,
    [Name] [Name] NOT NULL,
    [StartTime] [time] NOT NULL,
    [EndTime] [time] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_Shift_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Purchasing].[ShipMethod](
    [ShipMethodID] [int] IDENTITY (1, 1) NOT NULL,
    [Name] [Name] NOT NULL,
    [ShipBase] [money] NOT NULL CONSTRAINT [DF_ShipMethod_ShipBase] DEFAULT (0.00),
    [ShipRate] [money] NOT NULL CONSTRAINT [DF_ShipMethod_ShipRate] DEFAULT (0.00),
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_ShipMethod_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ShipMethod_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_ShipMethod_ShipBase] CHECK ([ShipBase] > 0.00),
    CONSTRAINT [CK_ShipMethod_ShipRate] CHECK ([ShipRate] > 0.00),
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[ShoppingCartItem](
    [ShoppingCartItemID] [int] IDENTITY (1, 1) NOT NULL,
    [ShoppingCartID] [nvarchar](50) NOT NULL,
    [Quantity] [int] NOT NULL CONSTRAINT [DF_ShoppingCartItem_Quantity] DEFAULT (1),
    [ProductID] [int] NOT NULL,
    [DateCreated] [datetime] NOT NULL CONSTRAINT [DF_ShoppingCartItem_DateCreated] DEFAULT (GETDATE()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_ShoppingCartItem_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_ShoppingCartItem_Quantity] CHECK ([Quantity] >= 1)
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[SpecialOffer](
    [SpecialOfferID] [int] IDENTITY (1, 1) NOT NULL,
    [Description] [nvarchar](255) NOT NULL,
    [DiscountPct] [smallmoney] NOT NULL CONSTRAINT [DF_SpecialOffer_DiscountPct] DEFAULT (0.00),
    [Type] [nvarchar](50) NOT NULL,
    [Category] [nvarchar](50) NOT NULL,
    [StartDate] [datetime] NOT NULL,
    [EndDate] [datetime] NOT NULL,
    [MinQty] [int] NOT NULL CONSTRAINT [DF_SpecialOffer_MinQty] DEFAULT (0),
    [MaxQty] [int] NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_SpecialOffer_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_SpecialOffer_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_SpecialOffer_EndDate] CHECK ([EndDate] >= [StartDate]),
    CONSTRAINT [CK_SpecialOffer_DiscountPct] CHECK ([DiscountPct] >= 0.00),
    CONSTRAINT [CK_SpecialOffer_MinQty] CHECK ([MinQty] >= 0),
    CONSTRAINT [CK_SpecialOffer_MaxQty]  CHECK ([MaxQty] >= 0)
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[SpecialOfferProduct](
    [SpecialOfferID] [int] NOT NULL,
    [ProductID] [int] NOT NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_SpecialOfferProduct_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_SpecialOfferProduct_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Person].[StateProvince](
    [StateProvinceID] [int] IDENTITY (1, 1) NOT NULL,
    [StateProvinceCode] [nchar](3) NOT NULL,
    [CountryRegionCode] [nvarchar](3) NOT NULL,
    [IsOnlyStateProvinceFlag] [Flag] NOT NULL CONSTRAINT [DF_StateProvince_IsOnlyStateProvinceFlag] DEFAULT (1),
    [Name] [Name] NOT NULL,
    [TerritoryID] [int] NOT NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_StateProvince_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_StateProvince_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Sales].[Store](
    [BusinessEntityID] [int] NOT NULL,
    [Name] [Name] NOT NULL,
    [SalesPersonID] [int] NULL,
    [Demographics] [XML]([Sales].[StoreSurveySchemaCollection]) NULL,
    [rowguid] uniqueidentifier ROWGUIDCOL NOT NULL CONSTRAINT [DF_Store_rowguid] DEFAULT (NEWID()),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_Store_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Production].[TransactionHistory](
    [TransactionID] [int] IDENTITY (100000, 1) NOT NULL,
    [ProductID] [int] NOT NULL,
    [ReferenceOrderID] [int] NOT NULL,
    [ReferenceOrderLineID] [int] NOT NULL CONSTRAINT [DF_TransactionHistory_ReferenceOrderLineID] DEFAULT (0),
    [TransactionDate] [datetime] NOT NULL CONSTRAINT [DF_TransactionHistory_TransactionDate] DEFAULT (GETDATE()),
    [TransactionType] [nchar](1) NOT NULL,
    [Quantity] [int] NOT NULL,
    [ActualCost] [money] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_TransactionHistory_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_TransactionHistory_TransactionType] CHECK (UPPER([TransactionType]) IN ('W', 'S', 'P'))
) ON [PRIMARY];
GO

CREATE TABLE [Production].[TransactionHistoryArchive](
    [TransactionID] [int] NOT NULL,
    [ProductID] [int] NOT NULL,
    [ReferenceOrderID] [int] NOT NULL,
    [ReferenceOrderLineID] [int] NOT NULL CONSTRAINT [DF_TransactionHistoryArchive_ReferenceOrderLineID] DEFAULT (0),
    [TransactionDate] [datetime] NOT NULL CONSTRAINT [DF_TransactionHistoryArchive_TransactionDate] DEFAULT (GETDATE()),
    [TransactionType] [nchar](1) NOT NULL,
    [Quantity] [int] NOT NULL,
    [ActualCost] [money] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_TransactionHistoryArchive_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_TransactionHistoryArchive_TransactionType] CHECK (UPPER([TransactionType]) IN ('W', 'S', 'P'))
) ON [PRIMARY];
GO

CREATE TABLE [Production].[UnitMeasure](
    [UnitMeasureCode] [nchar](3) NOT NULL,
    [Name] [Name] NOT NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_UnitMeasure_ModifiedDate] DEFAULT (GETDATE())
) ON [PRIMARY];
GO

CREATE TABLE [Purchasing].[Vendor](
    [BusinessEntityID] [int] NOT NULL,
    [AccountNumber] [AccountNumber] NOT NULL,
    [Name] [Name] NOT NULL,
    [CreditRating] [tinyint] NOT NULL,
    [PreferredVendorStatus] [Flag] NOT NULL CONSTRAINT [DF_Vendor_PreferredVendorStatus] DEFAULT (1),
    [ActiveFlag] [Flag] NOT NULL CONSTRAINT [DF_Vendor_ActiveFlag] DEFAULT (1),
    [PurchasingWebServiceURL] [nvarchar](1024) NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_Vendor_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_Vendor_CreditRating] CHECK ([CreditRating] BETWEEN 1 AND 5)
) ON [PRIMARY];
GO

CREATE TABLE [Production].[WorkOrder](
    [WorkOrderID] [int] IDENTITY (1, 1) NOT NULL,
    [ProductID] [int] NOT NULL,
    [OrderQty] [int] NOT NULL,
    [StockedQty] AS ISNULL([OrderQty] - [ScrappedQty], 0),
    [ScrappedQty] [smallint] NOT NULL,
    [StartDate] [datetime] NOT NULL,
    [EndDate] [datetime] NULL,
    [DueDate] [datetime] NOT NULL,
    [ScrapReasonID] [smallint] NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_WorkOrder_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_WorkOrder_OrderQty] CHECK ([OrderQty] > 0),
    CONSTRAINT [CK_WorkOrder_ScrappedQty] CHECK ([ScrappedQty] >= 0),
    CONSTRAINT [CK_WorkOrder_EndDate] CHECK (([EndDate] >= [StartDate]) OR ([EndDate] IS NULL))
) ON [PRIMARY];
GO

CREATE TABLE [Production].[WorkOrderRouting](
    [WorkOrderID] [int] NOT NULL,
    [ProductID] [int] NOT NULL,
    [OperationSequence] [smallint] NOT NULL,
    [LocationID] [smallint] NOT NULL,
    [ScheduledStartDate] [datetime] NOT NULL,
    [ScheduledEndDate] [datetime] NOT NULL,
    [ActualStartDate] [datetime] NULL,
    [ActualEndDate] [datetime] NULL,
    [ActualResourceHrs] [decimal](9, 4) NULL,
    [PlannedCost] [money] NOT NULL,
    [ActualCost] [money] NULL,
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_WorkOrderRouting_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_WorkOrderRouting_ScheduledEndDate] CHECK ([ScheduledEndDate] >= [ScheduledStartDate]),
    CONSTRAINT [CK_WorkOrderRouting_ActualEndDate] CHECK (([ActualEndDate] >= [ActualStartDate])
        OR ([ActualEndDate] IS NULL) OR ([ActualStartDate] IS NULL)),
    CONSTRAINT [CK_WorkOrderRouting_ActualResourceHrs] CHECK ([ActualResourceHrs] >= 0.0000),
    CONSTRAINT [CK_WorkOrderRouting_PlannedCost] CHECK ([PlannedCost] > 0.00),
    CONSTRAINT [CK_WorkOrderRouting_ActualCost] CHECK ([ActualCost] > 0.00)
) ON [PRIMARY];
GO

-- ******************************************************
-- Add Primary Keys
-- ******************************************************
PRINT '';
PRINT '*** Adding Primary Keys';
GO

SET QUOTED_IDENTIFIER ON;

ALTER TABLE [Person].[Address] WITH CHECK ADD
    CONSTRAINT [PK_Address_AddressID] PRIMARY KEY CLUSTERED
    (
        [AddressID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Person].[AddressType] WITH CHECK ADD
    CONSTRAINT [PK_AddressType_AddressTypeID] PRIMARY KEY CLUSTERED
    (
        [AddressTypeID]
    )  ON [PRIMARY];
GO

ALTER TABLE [dbo].[AWBuildVersion] WITH CHECK ADD
    CONSTRAINT [PK_AWBuildVersion_SystemInformationID] PRIMARY KEY CLUSTERED
    (
        [SystemInformationID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[BillOfMaterials] WITH CHECK ADD
    CONSTRAINT [PK_BillOfMaterials_BillOfMaterialsID] PRIMARY KEY NONCLUSTERED
    (
        [BillOfMaterialsID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Person].[BusinessEntity] WITH CHECK ADD
    CONSTRAINT [PK_BusinessEntity_BusinessEntityID] PRIMARY KEY CLUSTERED
    (
        [BusinessEntityID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Person].[BusinessEntityAddress] WITH CHECK ADD
    CONSTRAINT [PK_BusinessEntityAddress_BusinessEntityID_AddressID_AddressTypeID] PRIMARY KEY CLUSTERED
    (
        [BusinessEntityID],
		[AddressID],
		[AddressTypeID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Person].[BusinessEntityContact] WITH CHECK ADD
    CONSTRAINT [PK_BusinessEntityContact_BusinessEntityID_PersonID_ContactTypeID] PRIMARY KEY CLUSTERED
    (
        [BusinessEntityID],
		[PersonID],
		[ContactTypeID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Person].[ContactType] WITH CHECK ADD
    CONSTRAINT [PK_ContactType_ContactTypeID] PRIMARY KEY CLUSTERED
    (
        [ContactTypeID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[CountryRegionCurrency] WITH CHECK ADD
    CONSTRAINT [PK_CountryRegionCurrency_CountryRegionCode_CurrencyCode] PRIMARY KEY CLUSTERED
    (
        [CountryRegionCode],
        [CurrencyCode]
    )  ON [PRIMARY];
GO

ALTER TABLE [Person].[CountryRegion] WITH CHECK ADD
    CONSTRAINT [PK_CountryRegion_CountryRegionCode] PRIMARY KEY CLUSTERED
    (
        [CountryRegionCode]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[CreditCard] WITH CHECK ADD
    CONSTRAINT [PK_CreditCard_CreditCardID] PRIMARY KEY CLUSTERED
    (
        [CreditCardID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[Culture] WITH CHECK ADD
    CONSTRAINT [PK_Culture_CultureID] PRIMARY KEY CLUSTERED
    (
        [CultureID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[Currency] WITH CHECK ADD
    CONSTRAINT [PK_Currency_CurrencyCode] PRIMARY KEY CLUSTERED
    (
        [CurrencyCode]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[CurrencyRate] WITH CHECK ADD
    CONSTRAINT [PK_CurrencyRate_CurrencyRateID] PRIMARY KEY CLUSTERED
    (
        [CurrencyRateID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[Customer] WITH CHECK ADD
    CONSTRAINT [PK_Customer_CustomerID] PRIMARY KEY CLUSTERED
    (
        [CustomerID]
    )  ON [PRIMARY];
GO

ALTER TABLE [dbo].[DatabaseLog] WITH CHECK ADD
    CONSTRAINT [PK_DatabaseLog_DatabaseLogID] PRIMARY KEY NONCLUSTERED
    (
        [DatabaseLogID]
    )  ON [PRIMARY];
GO

ALTER TABLE [HumanResources].[Department] WITH CHECK ADD
    CONSTRAINT [PK_Department_DepartmentID] PRIMARY KEY CLUSTERED
    (
        [DepartmentID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[Document] WITH CHECK ADD
    CONSTRAINT [PK_Document_DocumentNode] PRIMARY KEY CLUSTERED
    (
        [DocumentNode]
    )  ON [PRIMARY];
GO

ALTER TABLE [Person].[EmailAddress] WITH CHECK ADD
    CONSTRAINT [PK_EmailAddress_BusinessEntityID_EmailAddressID] PRIMARY KEY CLUSTERED
    (
        [BusinessEntityID],
		[EmailAddressID]
    )  ON [PRIMARY];
GO

ALTER TABLE [HumanResources].[Employee] WITH CHECK ADD
    CONSTRAINT [PK_Employee_BusinessEntityID] PRIMARY KEY CLUSTERED
    (
        [BusinessEntityID]
    )  ON [PRIMARY];
GO

ALTER TABLE [HumanResources].[EmployeeDepartmentHistory] WITH CHECK ADD
    CONSTRAINT [PK_EmployeeDepartmentHistory_BusinessEntityID_StartDate_DepartmentID] PRIMARY KEY CLUSTERED
    (
        [BusinessEntityID],
        [StartDate],
        [DepartmentID],
        [ShiftID]
    )  ON [PRIMARY];
GO

ALTER TABLE [HumanResources].[EmployeePayHistory] WITH CHECK ADD
    CONSTRAINT [PK_EmployeePayHistory_BusinessEntityID_RateChangeDate] PRIMARY KEY CLUSTERED
    (
        [BusinessEntityID],
        [RateChangeDate]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[Illustration] WITH CHECK ADD
    CONSTRAINT [PK_Illustration_IllustrationID] PRIMARY KEY CLUSTERED
    (
        [IllustrationID]
    )  ON [PRIMARY];
GO

ALTER TABLE [HumanResources].[JobCandidate] WITH CHECK ADD
    CONSTRAINT [PK_JobCandidate_JobCandidateID] PRIMARY KEY CLUSTERED
    (
        [JobCandidateID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[Location] WITH CHECK ADD
    CONSTRAINT [PK_Location_LocationID] PRIMARY KEY CLUSTERED
    (
        [LocationID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Person].[Password] WITH CHECK ADD
    CONSTRAINT [PK_Password_BusinessEntityID] PRIMARY KEY CLUSTERED
    (
        [BusinessEntityID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Person].[Person] WITH CHECK ADD
    CONSTRAINT [PK_Person_BusinessEntityID] PRIMARY KEY CLUSTERED
    (
        [BusinessEntityID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[PersonCreditCard] WITH CHECK ADD
    CONSTRAINT [PK_PersonCreditCard_BusinessEntityID_CreditCardID] PRIMARY KEY CLUSTERED
    (
        [BusinessEntityID],
        [CreditCardID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Person].[PersonPhone] WITH CHECK ADD
    CONSTRAINT [PK_PersonPhone_BusinessEntityID_PhoneNumber_PhoneNumberTypeID] PRIMARY KEY CLUSTERED
    (
        [BusinessEntityID],
        [PhoneNumber],
        [PhoneNumberTypeID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Person].[PhoneNumberType] WITH CHECK ADD
    CONSTRAINT [PK_PhoneNumberType_PhoneNumberTypeID] PRIMARY KEY CLUSTERED
    (
        [PhoneNumberTypeID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[Product] WITH CHECK ADD
    CONSTRAINT [PK_Product_ProductID] PRIMARY KEY CLUSTERED
    (
        [ProductID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[ProductCategory] WITH CHECK ADD
    CONSTRAINT [PK_ProductCategory_ProductCategoryID] PRIMARY KEY CLUSTERED
    (
        [ProductCategoryID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[ProductCostHistory] WITH CHECK ADD
    CONSTRAINT [PK_ProductCostHistory_ProductID_StartDate] PRIMARY KEY CLUSTERED
    (
        [ProductID],
        [StartDate]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[ProductDescription] WITH CHECK ADD
    CONSTRAINT [PK_ProductDescription_ProductDescriptionID] PRIMARY KEY CLUSTERED
    (
        [ProductDescriptionID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[ProductDocument] WITH CHECK ADD
    CONSTRAINT [PK_ProductDocument_ProductID_DocumentNode] PRIMARY KEY CLUSTERED
    (
        [ProductID],
        [DocumentNode]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[ProductInventory] WITH CHECK ADD
    CONSTRAINT [PK_ProductInventory_ProductID_LocationID] PRIMARY KEY CLUSTERED
    (
    [ProductID],
    [LocationID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[ProductListPriceHistory] WITH CHECK ADD
    CONSTRAINT [PK_ProductListPriceHistory_ProductID_StartDate] PRIMARY KEY CLUSTERED
    (
        [ProductID],
        [StartDate]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[ProductModel] WITH CHECK ADD
    CONSTRAINT [PK_ProductModel_ProductModelID] PRIMARY KEY CLUSTERED
    (
        [ProductModelID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[ProductModelIllustration] WITH CHECK ADD
    CONSTRAINT [PK_ProductModelIllustration_ProductModelID_IllustrationID] PRIMARY KEY CLUSTERED
    (
        [ProductModelID],
        [IllustrationID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[ProductModelProductDescriptionCulture] WITH CHECK ADD
    CONSTRAINT [PK_ProductModelProductDescriptionCulture_ProductModelID_ProductDescriptionID_CultureID] PRIMARY KEY CLUSTERED
    (
        [ProductModelID],
        [ProductDescriptionID],
        [CultureID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[ProductPhoto] WITH CHECK ADD
    CONSTRAINT [PK_ProductPhoto_ProductPhotoID] PRIMARY KEY CLUSTERED
    (
        [ProductPhotoID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[ProductProductPhoto] WITH CHECK ADD
    CONSTRAINT [PK_ProductProductPhoto_ProductID_ProductPhotoID] PRIMARY KEY NONCLUSTERED
    (
        [ProductID],
        [ProductPhotoID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[ProductReview] WITH CHECK ADD
    CONSTRAINT [PK_ProductReview_ProductReviewID] PRIMARY KEY CLUSTERED
    (
        [ProductReviewID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[ProductSubcategory] WITH CHECK ADD
    CONSTRAINT [PK_ProductSubcategory_ProductSubcategoryID] PRIMARY KEY CLUSTERED
    (
        [ProductSubcategoryID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Purchasing].[ProductVendor] WITH CHECK ADD
    CONSTRAINT [PK_ProductVendor_ProductID_BusinessEntityID] PRIMARY KEY CLUSTERED
    (
        [ProductID],
        [BusinessEntityID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Purchasing].[PurchaseOrderDetail] WITH CHECK ADD
    CONSTRAINT [PK_PurchaseOrderDetail_PurchaseOrderID_PurchaseOrderDetailID] PRIMARY KEY CLUSTERED
    (
        [PurchaseOrderID],
        [PurchaseOrderDetailID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Purchasing].[PurchaseOrderHeader] WITH CHECK ADD
    CONSTRAINT [PK_PurchaseOrderHeader_PurchaseOrderID] PRIMARY KEY CLUSTERED
    (
        [PurchaseOrderID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[SalesOrderDetail] WITH CHECK ADD
    CONSTRAINT [PK_SalesOrderDetail_SalesOrderID_SalesOrderDetailID] PRIMARY KEY CLUSTERED
    (
        [SalesOrderID],
        [SalesOrderDetailID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[SalesOrderHeader] WITH CHECK ADD
    CONSTRAINT [PK_SalesOrderHeader_SalesOrderID] PRIMARY KEY CLUSTERED
    (
        [SalesOrderID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[SalesOrderHeaderSalesReason] WITH CHECK ADD
    CONSTRAINT [PK_SalesOrderHeaderSalesReason_SalesOrderID_SalesReasonID] PRIMARY KEY CLUSTERED
    (
        [SalesOrderID],
        [SalesReasonID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[SalesPerson] WITH CHECK ADD
    CONSTRAINT [PK_SalesPerson_BusinessEntityID] PRIMARY KEY CLUSTERED
    (
        [BusinessEntityID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[SalesPersonQuotaHistory] WITH CHECK ADD
    CONSTRAINT [PK_SalesPersonQuotaHistory_BusinessEntityID_QuotaDate] PRIMARY KEY CLUSTERED
    (
        [BusinessEntityID],
        [QuotaDate] --,
        -- [ProductCategoryID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[SalesReason] WITH CHECK ADD
    CONSTRAINT [PK_SalesReason_SalesReasonID] PRIMARY KEY CLUSTERED
    (
        [SalesReasonID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[SalesTaxRate] WITH CHECK ADD
    CONSTRAINT [PK_SalesTaxRate_SalesTaxRateID] PRIMARY KEY CLUSTERED
    (
        [SalesTaxRateID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[SalesTerritory] WITH CHECK ADD
    CONSTRAINT [PK_SalesTerritory_TerritoryID] PRIMARY KEY CLUSTERED
    (
        [TerritoryID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[SalesTerritoryHistory] WITH CHECK ADD
    CONSTRAINT [PK_SalesTerritoryHistory_BusinessEntityID_StartDate_TerritoryID] PRIMARY KEY CLUSTERED
    (
        [BusinessEntityID],  --Sales person
        [StartDate],
        [TerritoryID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[ScrapReason] WITH CHECK ADD
    CONSTRAINT [PK_ScrapReason_ScrapReasonID] PRIMARY KEY CLUSTERED
    (
        [ScrapReasonID]
    )  ON [PRIMARY];
GO

ALTER TABLE [HumanResources].[Shift] WITH CHECK ADD
    CONSTRAINT [PK_Shift_ShiftID] PRIMARY KEY CLUSTERED
    (
        [ShiftID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Purchasing].[ShipMethod] WITH CHECK ADD
    CONSTRAINT [PK_ShipMethod_ShipMethodID] PRIMARY KEY CLUSTERED
    (
        [ShipMethodID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[ShoppingCartItem] WITH CHECK ADD
    CONSTRAINT [PK_ShoppingCartItem_ShoppingCartItemID] PRIMARY KEY CLUSTERED
    (
        [ShoppingCartItemID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[SpecialOffer] WITH CHECK ADD
    CONSTRAINT [PK_SpecialOffer_SpecialOfferID] PRIMARY KEY CLUSTERED
    (
        [SpecialOfferID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[SpecialOfferProduct] WITH CHECK ADD
    CONSTRAINT [PK_SpecialOfferProduct_SpecialOfferID_ProductID] PRIMARY KEY CLUSTERED
    (
        [SpecialOfferID],
        [ProductID]
    )  ON [PRIMARY];
GO
GO

ALTER TABLE [Person].[StateProvince] WITH CHECK ADD
    CONSTRAINT [PK_StateProvince_StateProvinceID] PRIMARY KEY CLUSTERED
    (
        [StateProvinceID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Sales].[Store] WITH CHECK ADD
    CONSTRAINT [PK_Store_BusinessEntityID] PRIMARY KEY CLUSTERED
    (
        [BusinessEntityID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[TransactionHistory] WITH CHECK ADD
    CONSTRAINT [PK_TransactionHistory_TransactionID] PRIMARY KEY CLUSTERED
    (
        [TransactionID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[TransactionHistoryArchive] WITH CHECK ADD
    CONSTRAINT [PK_TransactionHistoryArchive_TransactionID] PRIMARY KEY CLUSTERED
    (
        [TransactionID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[UnitMeasure] WITH CHECK ADD
    CONSTRAINT [PK_UnitMeasure_UnitMeasureCode] PRIMARY KEY CLUSTERED
    (
        [UnitMeasureCode]
    )  ON [PRIMARY];
GO

ALTER TABLE [Purchasing].[Vendor] WITH CHECK ADD
    CONSTRAINT [PK_Vendor_BusinessEntityID] PRIMARY KEY CLUSTERED
    (
        [BusinessEntityID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[WorkOrder] WITH CHECK ADD
    CONSTRAINT [PK_WorkOrder_WorkOrderID] PRIMARY KEY CLUSTERED
    (
        [WorkOrderID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[WorkOrderRouting] WITH CHECK ADD
    CONSTRAINT [PK_WorkOrderRouting_WorkOrderID_ProductID_OperationSequence] PRIMARY KEY CLUSTERED
    (
        [WorkOrderID],
        [ProductID],
        [OperationSequence]
    )  ON [PRIMARY];
GO

-- ******************************************************
-- Add Indexes
-- ******************************************************
PRINT '';
PRINT '*** Adding Indexes';
GO

CREATE UNIQUE INDEX [AK_Address_rowguid] ON [Person].[Address]([rowguid]) ON [PRIMARY];
CREATE UNIQUE INDEX [IX_Address_AddressLine1_AddressLine2_City_StateProvinceID_PostalCode] ON [Person].[Address] ([AddressLine1], [AddressLine2], [City], [StateProvinceID], [PostalCode]) ON [PRIMARY];
CREATE INDEX [IX_Address_StateProvinceID] ON [Person].[Address]([StateProvinceID]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_AddressType_rowguid] ON [Person].[AddressType]([rowguid]) ON [PRIMARY];
CREATE UNIQUE INDEX [AK_AddressType_Name] ON [Person].[AddressType]([Name]) ON [PRIMARY];
GO

CREATE INDEX [IX_BillOfMaterials_UnitMeasureCode] ON [Production].[BillOfMaterials]([UnitMeasureCode]) ON [PRIMARY];
CREATE UNIQUE CLUSTERED INDEX [AK_BillOfMaterials_ProductAssemblyID_ComponentID_StartDate] ON [Production].[BillOfMaterials]([ProductAssemblyID], [ComponentID], [StartDate]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_BusinessEntity_rowguid] ON [Person].[BusinessEntity]([rowguid]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_BusinessEntityAddress_rowguid] ON [Person].[BusinessEntityAddress]([rowguid]) ON [PRIMARY];
CREATE INDEX [IX_BusinessEntityAddress_AddressID] ON [Person].[BusinessEntityAddress]([AddressID]) ON [PRIMARY];
CREATE INDEX [IX_BusinessEntityAddress_AddressTypeID] ON [Person].[BusinessEntityAddress]([AddressTypeID]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_BusinessEntityContact_rowguid] ON [Person].[BusinessEntityContact]([rowguid]) ON [PRIMARY];
CREATE INDEX [IX_BusinessEntityContact_PersonID] ON [Person].[BusinessEntityContact]([PersonID]) ON [PRIMARY];
CREATE INDEX [IX_BusinessEntityContact_ContactTypeID] ON [Person].[BusinessEntityContact]([ContactTypeID]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_ContactType_Name] ON [Person].[ContactType]([Name]) ON [PRIMARY];
GO

CREATE INDEX [IX_CountryRegionCurrency_CurrencyCode] ON [Sales].[CountryRegionCurrency]([CurrencyCode]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_CountryRegion_Name] ON [Person].[CountryRegion]([Name]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_CreditCard_CardNumber] ON [Sales].[CreditCard]([CardNumber]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_Culture_Name] ON [Production].[Culture]([Name]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_Currency_Name] ON [Sales].[Currency]([Name]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_CurrencyRate_CurrencyRateDate_FromCurrencyCode_ToCurrencyCode] ON [Sales].[CurrencyRate]([CurrencyRateDate], [FromCurrencyCode], [ToCurrencyCode]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_Customer_rowguid] ON [Sales].[Customer]([rowguid]) ON [PRIMARY];
CREATE UNIQUE INDEX [AK_Customer_AccountNumber] ON [Sales].[Customer]([AccountNumber]) ON [PRIMARY];
CREATE INDEX [IX_Customer_TerritoryID] ON [Sales].[Customer]([TerritoryID]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_Department_Name] ON [HumanResources].[Department]([Name]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_Document_DocumentLevel_DocumentNode] ON [Production].[Document] ([DocumentLevel], [DocumentNode]);
CREATE UNIQUE INDEX [AK_Document_rowguid] ON [Production].[Document]([rowguid]) ON [PRIMARY];
CREATE INDEX [IX_Document_FileName_Revision] ON [Production].[Document]([FileName], [Revision]) ON [PRIMARY];
GO

CREATE INDEX [IX_EmailAddress_EmailAddress] ON [Person].[EmailAddress]([EmailAddress]) ON [PRIMARY];
GO

CREATE INDEX [IX_Employee_OrganizationNode] ON [HumanResources].[Employee] ([OrganizationNode]);
CREATE INDEX [IX_Employee_OrganizationLevel_OrganizationNode] ON [HumanResources].[Employee] ([OrganizationLevel], [OrganizationNode]);
CREATE UNIQUE INDEX [AK_Employee_LoginID] ON [HumanResources].[Employee]([LoginID]) ON [PRIMARY];
CREATE UNIQUE INDEX [AK_Employee_NationalIDNumber] ON [HumanResources].[Employee]([NationalIDNumber]) ON [PRIMARY];
CREATE UNIQUE INDEX [AK_Employee_rowguid] ON [HumanResources].[Employee]([rowguid]) ON [PRIMARY];
GO

CREATE INDEX [IX_EmployeeDepartmentHistory_DepartmentID] ON [HumanResources].[EmployeeDepartmentHistory]([DepartmentID]) ON [PRIMARY];
CREATE INDEX [IX_EmployeeDepartmentHistory_ShiftID] ON [HumanResources].[EmployeeDepartmentHistory]([ShiftID]) ON [PRIMARY];
GO

CREATE INDEX [IX_JobCandidate_BusinessEntityID] ON [HumanResources].[JobCandidate]([BusinessEntityID]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_Location_Name] ON [Production].[Location]([Name]) ON [PRIMARY];
GO

CREATE INDEX [IX_Person_LastName_FirstName_MiddleName] ON [Person].[Person] ([LastName], [FirstName], [MiddleName]) ON [PRIMARY];
CREATE UNIQUE INDEX [AK_Person_rowguid] ON [Person].[Person]([rowguid]) ON [PRIMARY];

CREATE INDEX [IX_PersonPhone_PhoneNumber] on [Person].[PersonPhone] ([PhoneNumber]) ON [PRIMARY];

CREATE UNIQUE INDEX [AK_Product_ProductNumber] ON [Production].[Product]([ProductNumber]) ON [PRIMARY];
CREATE UNIQUE INDEX [AK_Product_Name] ON [Production].[Product]([Name]) ON [PRIMARY];
CREATE UNIQUE INDEX [AK_Product_rowguid] ON [Production].[Product]([rowguid]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_ProductCategory_Name] ON [Production].[ProductCategory]([Name]) ON [PRIMARY];
CREATE UNIQUE INDEX [AK_ProductCategory_rowguid] ON [Production].[ProductCategory]([rowguid]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_ProductDescription_rowguid] ON [Production].[ProductDescription]([rowguid]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_ProductModel_Name] ON [Production].[ProductModel]([Name]) ON [PRIMARY];
CREATE UNIQUE INDEX [AK_ProductModel_rowguid] ON [Production].[ProductModel]([rowguid]) ON [PRIMARY];
GO

CREATE NONCLUSTERED INDEX [IX_ProductReview_ProductID_Name] ON [Production].[ProductReview]([ProductID], [ReviewerName]) INCLUDE ([Comments]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_ProductSubcategory_Name] ON [Production].[ProductSubcategory]([Name]) ON [PRIMARY];
CREATE UNIQUE INDEX [AK_ProductSubcategory_rowguid] ON [Production].[ProductSubcategory]([rowguid]) ON [PRIMARY];
GO

CREATE INDEX [IX_ProductVendor_UnitMeasureCode] ON [Purchasing].[ProductVendor]([UnitMeasureCode]) ON [PRIMARY];
CREATE INDEX [IX_ProductVendor_BusinessEntityID] ON [Purchasing].[ProductVendor]([BusinessEntityID]) ON [PRIMARY];
GO

CREATE INDEX [IX_PurchaseOrderDetail_ProductID] ON [Purchasing].[PurchaseOrderDetail]([ProductID]) ON [PRIMARY];
GO

CREATE INDEX [IX_PurchaseOrderHeader_VendorID] ON [Purchasing].[PurchaseOrderHeader]([VendorID]) ON [PRIMARY];
CREATE INDEX [IX_PurchaseOrderHeader_EmployeeID] ON [Purchasing].[PurchaseOrderHeader]([EmployeeID]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_SalesOrderDetail_rowguid] ON [Sales].[SalesOrderDetail]([rowguid]) ON [PRIMARY];
CREATE INDEX [IX_SalesOrderDetail_ProductID] ON [Sales].[SalesOrderDetail]([ProductID]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_SalesOrderHeader_rowguid] ON [Sales].[SalesOrderHeader]([rowguid]) ON [PRIMARY];
CREATE UNIQUE INDEX [AK_SalesOrderHeader_SalesOrderNumber] ON [Sales].[SalesOrderHeader]([SalesOrderNumber]) ON [PRIMARY];
CREATE INDEX [IX_SalesOrderHeader_CustomerID] ON [Sales].[SalesOrderHeader]([CustomerID]) ON [PRIMARY];
CREATE INDEX [IX_SalesOrderHeader_SalesPersonID] ON [Sales].[SalesOrderHeader]([SalesPersonID]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_SalesPerson_rowguid] ON [Sales].[SalesPerson]([rowguid]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_SalesPersonQuotaHistory_rowguid] ON [Sales].[SalesPersonQuotaHistory]([rowguid]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_SalesTaxRate_StateProvinceID_TaxType] ON [Sales].[SalesTaxRate]([StateProvinceID], [TaxType]) ON [PRIMARY];
CREATE UNIQUE INDEX [AK_SalesTaxRate_rowguid] ON [Sales].[SalesTaxRate]([rowguid]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_SalesTerritory_Name] ON [Sales].[SalesTerritory]([Name]) ON [PRIMARY];
CREATE UNIQUE INDEX [AK_SalesTerritory_rowguid] ON [Sales].[SalesTerritory]([rowguid]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_SalesTerritoryHistory_rowguid] ON [Sales].[SalesTerritoryHistory]([rowguid]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_ScrapReason_Name] ON [Production].[ScrapReason]([Name]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_Shift_Name] ON [HumanResources].[Shift]([Name]) ON [PRIMARY];
CREATE UNIQUE INDEX [AK_Shift_StartTime_EndTime] ON [HumanResources].[Shift]([StartTime], [EndTime]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_ShipMethod_Name] ON [Purchasing].[ShipMethod]([Name]) ON [PRIMARY];
CREATE UNIQUE INDEX [AK_ShipMethod_rowguid] ON [Purchasing].[ShipMethod]([rowguid]) ON [PRIMARY];
GO

CREATE INDEX [IX_ShoppingCartItem_ShoppingCartID_ProductID] ON [Sales].[ShoppingCartItem]([ShoppingCartID], [ProductID]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_SpecialOffer_rowguid] ON [Sales].[SpecialOffer]([rowguid]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_SpecialOfferProduct_rowguid] ON [Sales].[SpecialOfferProduct]([rowguid]) ON [PRIMARY];
CREATE INDEX [IX_SpecialOfferProduct_ProductID] ON [Sales].[SpecialOfferProduct]([ProductID]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_StateProvince_Name] ON [Person].[StateProvince]([Name]) ON [PRIMARY];
CREATE UNIQUE INDEX [AK_StateProvince_StateProvinceCode_CountryRegionCode] ON [Person].[StateProvince]([StateProvinceCode], [CountryRegionCode]) ON [PRIMARY];
CREATE UNIQUE INDEX [AK_StateProvince_rowguid] ON [Person].[StateProvince]([rowguid]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_Store_rowguid] ON [Sales].[Store]([rowguid]) ON [PRIMARY];
CREATE INDEX [IX_Store_SalesPersonID] ON [Sales].[Store]([SalesPersonID]) ON [PRIMARY];
GO

CREATE INDEX [IX_TransactionHistory_ProductID] ON [Production].[TransactionHistory]([ProductID]) ON [PRIMARY];
CREATE INDEX [IX_TransactionHistory_ReferenceOrderID_ReferenceOrderLineID] ON [Production].[TransactionHistory]([ReferenceOrderID], [ReferenceOrderLineID]) ON [PRIMARY];
GO

CREATE INDEX [IX_TransactionHistoryArchive_ProductID] ON [Production].[TransactionHistoryArchive]([ProductID]) ON [PRIMARY];
CREATE INDEX [IX_TransactionHistoryArchive_ReferenceOrderID_ReferenceOrderLineID] ON [Production].[TransactionHistoryArchive]([ReferenceOrderID], [ReferenceOrderLineID]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_UnitMeasure_Name] ON [Production].[UnitMeasure]([Name]) ON [PRIMARY];
GO

CREATE UNIQUE INDEX [AK_Vendor_AccountNumber] ON [Purchasing].[Vendor]([AccountNumber]) ON [PRIMARY];
GO

CREATE INDEX [IX_WorkOrder_ScrapReasonID] ON [Production].[WorkOrder]([ScrapReasonID]) ON [PRIMARY];
CREATE INDEX [IX_WorkOrder_ProductID] ON [Production].[WorkOrder]([ProductID]) ON [PRIMARY];
GO

CREATE INDEX [IX_WorkOrderRouting_ProductID] ON [Production].[WorkOrderRouting]([ProductID]) ON [PRIMARY];
GO

-- ****************************************
-- Create XML index for each XML column
-- ****************************************
PRINT '';
PRINT '*** Creating XML index for each XML column';
GO

SET ARITHABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

CREATE PRIMARY XML INDEX [PXML_Person_AddContact] ON [Person].[Person]([AdditionalContactInfo]);
GO

CREATE PRIMARY XML INDEX [PXML_Person_Demographics] ON [Person].[Person]([Demographics]);
GO

CREATE XML INDEX [XMLPATH_Person_Demographics] ON [Person].[Person]([Demographics])
USING XML INDEX [PXML_Person_Demographics] FOR PATH;
GO

CREATE XML INDEX [XMLPROPERTY_Person_Demographics] ON [Person].[Person]([Demographics])
USING XML INDEX [PXML_Person_Demographics] FOR PROPERTY;
GO

CREATE XML INDEX [XMLVALUE_Person_Demographics] ON [Person].[Person]([Demographics])
USING XML INDEX [PXML_Person_Demographics] FOR VALUE;
GO

CREATE PRIMARY XML INDEX [PXML_Store_Demographics] ON [Sales].[Store]([Demographics]);
GO

CREATE PRIMARY XML INDEX [PXML_ProductModel_CatalogDescription] ON [Production].[ProductModel]([CatalogDescription]);
GO

CREATE PRIMARY XML INDEX [PXML_ProductModel_Instructions] ON [Production].[ProductModel]([Instructions]);
GO

-- ****************************************
-- Create Foreign key constraints
-- ****************************************
PRINT '';
PRINT '*** Creating Foreign Key Constraints';
GO

ALTER TABLE [Person].[Address] ADD
    CONSTRAINT [FK_Address_StateProvince_StateProvinceID] FOREIGN KEY
    (
        [StateProvinceID]
    ) REFERENCES [Person].[StateProvince](
        [StateProvinceID]
    );
GO

ALTER TABLE [Production].[BillOfMaterials] ADD
    CONSTRAINT [FK_BillOfMaterials_Product_ProductAssemblyID] FOREIGN KEY
    (
        [ProductAssemblyID]
    ) REFERENCES [Production].[Product](
        [ProductID]
    ),
    CONSTRAINT [FK_BillOfMaterials_Product_ComponentID] FOREIGN KEY
    (
        [ComponentID]
    ) REFERENCES [Production].[Product](
        [ProductID]
    ),
    CONSTRAINT [FK_BillOfMaterials_UnitMeasure_UnitMeasureCode] FOREIGN KEY
    (
        [UnitMeasureCode]
    ) REFERENCES [Production].[UnitMeasure](
        [UnitMeasureCode]
    );
GO

ALTER TABLE [Person].[BusinessEntityAddress] ADD
    CONSTRAINT [FK_BusinessEntityAddress_Address_AddressID] FOREIGN KEY
    (
        [AddressID]
    ) REFERENCES [Person].[Address](
        [AddressID]
    ),
    CONSTRAINT [FK_BusinessEntityAddress_AddressType_AddressTypeID] FOREIGN KEY
    (
        [AddressTypeID]
    ) REFERENCES [Person].[AddressType](
        [AddressTypeID]
    ),
    CONSTRAINT [FK_BusinessEntityAddress_BusinessEntity_BusinessEntityID] FOREIGN KEY
    (
        [BusinessEntityID]
    ) REFERENCES [Person].[BusinessEntity](
        [BusinessEntityID]
    );
GO

ALTER TABLE [Person].[BusinessEntityContact] ADD
    CONSTRAINT [FK_BusinessEntityContact_Person_PersonID] FOREIGN KEY
    (
        [PersonID]
    ) REFERENCES [Person].[Person](
        [BusinessEntityID]
    ),
    CONSTRAINT [FK_BusinessEntityContact_ContactType_ContactTypeID] FOREIGN KEY
    (
        [ContactTypeID]
    ) REFERENCES [Person].[ContactType](
        [ContactTypeID]
    ),
    CONSTRAINT [FK_BusinessEntityContact_BusinessEntity_BusinessEntityID] FOREIGN KEY
    (
        [BusinessEntityID]
    ) REFERENCES [Person].[BusinessEntity](
        [BusinessEntityID]
    );
GO

ALTER TABLE [Sales].[CountryRegionCurrency] ADD
    CONSTRAINT [FK_CountryRegionCurrency_CountryRegion_CountryRegionCode] FOREIGN KEY
    (
        [CountryRegionCode]
    ) REFERENCES [Person].[CountryRegion](
        [CountryRegionCode]
    ),
    CONSTRAINT [FK_CountryRegionCurrency_Currency_CurrencyCode] FOREIGN KEY
    (
        [CurrencyCode]
    ) REFERENCES [Sales].[Currency](
        [CurrencyCode]
    );
GO

ALTER TABLE [Sales].[CurrencyRate] ADD
    CONSTRAINT [FK_CurrencyRate_Currency_FromCurrencyCode] FOREIGN KEY
    (
        [FromCurrencyCode]
    ) REFERENCES [Sales].[Currency](
        [CurrencyCode]
    ),
    CONSTRAINT [FK_CurrencyRate_Currency_ToCurrencyCode] FOREIGN KEY
    (
        [ToCurrencyCode]
    ) REFERENCES [Sales].[Currency](
        [CurrencyCode]
    );
GO

ALTER TABLE [Sales].[Customer] ADD
    CONSTRAINT [FK_Customer_Person_PersonID] FOREIGN KEY
    (
        [PersonID]
    ) REFERENCES [Person].[Person](
        [BusinessEntityID]
    ),
    CONSTRAINT [FK_Customer_Store_StoreID] FOREIGN KEY
    (
        [StoreID]
    ) REFERENCES [Sales].[Store](
        [BusinessEntityID]
    ),
    CONSTRAINT [FK_Customer_SalesTerritory_TerritoryID] FOREIGN KEY
    (
        [TerritoryID]
    ) REFERENCES [Sales].[SalesTerritory](
        [TerritoryID]
    );
GO

ALTER TABLE [Production].[Document] ADD
	CONSTRAINT [FK_Document_Employee_Owner] FOREIGN KEY
	(
		[Owner]
	) REFERENCES [HumanResources].[Employee](
		[BusinessEntityID]
	);
GO

ALTER TABLE [Person].[EmailAddress] ADD
    CONSTRAINT [FK_EmailAddress_Person_BusinessEntityID] FOREIGN KEY
    (
        [BusinessEntityID]
    ) REFERENCES [Person].[Person](
        [BusinessEntityID]
    );
GO

ALTER TABLE [HumanResources].[Employee] ADD
    CONSTRAINT [FK_Employee_Person_BusinessEntityID] FOREIGN KEY
    (
        [BusinessEntityID]
    ) REFERENCES [Person].[Person](
        [BusinessEntityID]
    );
GO

ALTER TABLE [HumanResources].[EmployeeDepartmentHistory] ADD
    CONSTRAINT [FK_EmployeeDepartmentHistory_Department_DepartmentID] FOREIGN KEY
    (
        [DepartmentID]
    ) REFERENCES [HumanResources].[Department](
        [DepartmentID]
    ),
    CONSTRAINT [FK_EmployeeDepartmentHistory_Employee_BusinessEntityID] FOREIGN KEY
    (
        [BusinessEntityID]
    ) REFERENCES [HumanResources].[Employee](
        [BusinessEntityID]
    ),
    CONSTRAINT [FK_EmployeeDepartmentHistory_Shift_ShiftID] FOREIGN KEY
    (
        [ShiftID]
    ) REFERENCES [HumanResources].[Shift](
        [ShiftID]
    );
GO

ALTER TABLE [HumanResources].[EmployeePayHistory] ADD
    CONSTRAINT [FK_EmployeePayHistory_Employee_BusinessEntityID] FOREIGN KEY
    (
        [BusinessEntityID]
    ) REFERENCES [HumanResources].[Employee](
        [BusinessEntityID]
    );
GO

ALTER TABLE [HumanResources].[JobCandidate] ADD
    CONSTRAINT [FK_JobCandidate_Employee_BusinessEntityID] FOREIGN KEY
    (
        [BusinessEntityID]
    ) REFERENCES [HumanResources].[Employee](
        [BusinessEntityID]
    );
GO

ALTER TABLE [Person].[Password] ADD
    CONSTRAINT [FK_Password_Person_BusinessEntityID] FOREIGN KEY
    (
        [BusinessEntityID]
    ) REFERENCES [Person].[Person](
        [BusinessEntityID]
    );
GO

ALTER TABLE [Person].[Person] ADD
    CONSTRAINT [FK_Person_BusinessEntity_BusinessEntityID] FOREIGN KEY
    (
        [BusinessEntityID]
    ) REFERENCES [Person].[BusinessEntity](
        [BusinessEntityID]
    );
GO

ALTER TABLE [Sales].[PersonCreditCard] ADD
    CONSTRAINT [FK_PersonCreditCard_Person_BusinessEntityID] FOREIGN KEY
    (
        [BusinessEntityID]
    ) REFERENCES [Person].[Person](
        [BusinessEntityID]
    ),
    CONSTRAINT [FK_PersonCreditCard_CreditCard_CreditCardID] FOREIGN KEY
    (
        [CreditCardID]
    ) REFERENCES [Sales].[CreditCard](
        [CreditCardID]
    );
GO

ALTER TABLE [Person].[PersonPhone] ADD
    CONSTRAINT [FK_PersonPhone_Person_BusinessEntityID] FOREIGN KEY
    (
        [BusinessEntityID]
    ) REFERENCES [Person].[Person](
        [BusinessEntityID]
    ),
 CONSTRAINT [FK_PersonPhone_PhoneNumberType_PhoneNumberTypeID] FOREIGN KEY
    (
        [PhoneNumberTypeID]
    ) REFERENCES [Person].[PhoneNumberType](
        [PhoneNumberTypeID]
    );
GO

ALTER TABLE [Production].[Product] ADD
    CONSTRAINT [FK_Product_UnitMeasure_SizeUnitMeasureCode] FOREIGN KEY
    (
        [SizeUnitMeasureCode]
    ) REFERENCES [Production].[UnitMeasure](
        [UnitMeasureCode]
    ),
    CONSTRAINT [FK_Product_UnitMeasure_WeightUnitMeasureCode] FOREIGN KEY
    (
        [WeightUnitMeasureCode]
    ) REFERENCES [Production].[UnitMeasure](
        [UnitMeasureCode]
    ),
    CONSTRAINT [FK_Product_ProductModel_ProductModelID] FOREIGN KEY
    (
        [ProductModelID]
    ) REFERENCES [Production].[ProductModel](
        [ProductModelID]
    ),
    CONSTRAINT [FK_Product_ProductSubcategory_ProductSubcategoryID] FOREIGN KEY
    (
        [ProductSubcategoryID]
    ) REFERENCES [Production].[ProductSubcategory](
        [ProductSubcategoryID]
    );
GO

ALTER TABLE [Production].[ProductCostHistory] ADD
    CONSTRAINT [FK_ProductCostHistory_Product_ProductID] FOREIGN KEY
    (
        [ProductID]
    ) REFERENCES [Production].[Product](
        [ProductID]
    );
GO

ALTER TABLE [Production].[ProductDocument] ADD
    CONSTRAINT [FK_ProductDocument_Product_ProductID] FOREIGN KEY
    (
        [ProductID]
    ) REFERENCES [Production].[Product](
        [ProductID]
    ),
    CONSTRAINT [FK_ProductDocument_Document_DocumentNode] FOREIGN KEY
    (
        [DocumentNode]
    ) REFERENCES [Production].[Document](
        [DocumentNode]
    );
GO

ALTER TABLE [Production].[ProductInventory] ADD
    CONSTRAINT [FK_ProductInventory_Location_LocationID] FOREIGN KEY
    (
        [LocationID]
    ) REFERENCES [Production].[Location](
        [LocationID]
    ),
    CONSTRAINT [FK_ProductInventory_Product_ProductID] FOREIGN KEY
    (
        [ProductID]
    ) REFERENCES [Production].[Product](
        [ProductID]
    );
GO

ALTER TABLE [Production].[ProductListPriceHistory] ADD
    CONSTRAINT [FK_ProductListPriceHistory_Product_ProductID] FOREIGN KEY
    (
        [ProductID]
    ) REFERENCES [Production].[Product](
        [ProductID]
    );
GO

ALTER TABLE [Production].[ProductModelIllustration] ADD
    CONSTRAINT [FK_ProductModelIllustration_ProductModel_ProductModelID] FOREIGN KEY
    (
        [ProductModelID]
    ) REFERENCES [Production].[ProductModel](
        [ProductModelID]
    ),
    CONSTRAINT [FK_ProductModelIllustration_Illustration_IllustrationID] FOREIGN KEY
    (
        [IllustrationID]
    ) REFERENCES [Production].[Illustration](
        [IllustrationID]
    );
GO

ALTER TABLE [Production].[ProductModelProductDescriptionCulture] ADD
    CONSTRAINT [FK_ProductModelProductDescriptionCulture_ProductDescription_ProductDescriptionID] FOREIGN KEY
    (
        [ProductDescriptionID]
    ) REFERENCES [Production].[ProductDescription](
        [ProductDescriptionID]
    ),
    CONSTRAINT [FK_ProductModelProductDescriptionCulture_Culture_CultureID] FOREIGN KEY
    (
        [CultureID]
    ) REFERENCES [Production].[Culture]
    (
        [CultureID]
    ),
    CONSTRAINT [FK_ProductModelProductDescriptionCulture_ProductModel_ProductModelID] FOREIGN KEY
    (
        [ProductModelID]
    ) REFERENCES [Production].[ProductModel](
        [ProductModelID]
    );
GO

ALTER TABLE [Production].[ProductProductPhoto] ADD
    CONSTRAINT [FK_ProductProductPhoto_Product_ProductID] FOREIGN KEY
    (
        [ProductID]
    ) REFERENCES [Production].[Product](
        [ProductID]
    ),
    CONSTRAINT [FK_ProductProductPhoto_ProductPhoto_ProductPhotoID] FOREIGN KEY
    (
        [ProductPhotoID]
    ) REFERENCES [Production].[ProductPhoto](
        [ProductPhotoID]
    );
GO

ALTER TABLE [Production].[ProductReview] ADD
    CONSTRAINT [FK_ProductReview_Product_ProductID] FOREIGN KEY
    (
        [ProductID]
    ) REFERENCES [Production].[Product](
        [ProductID]
    );
GO

ALTER TABLE [Production].[ProductSubcategory] ADD
    CONSTRAINT [FK_ProductSubcategory_ProductCategory_ProductCategoryID] FOREIGN KEY
    (
        [ProductCategoryID]
    ) REFERENCES [Production].[ProductCategory](
        [ProductCategoryID]
    );
GO

ALTER TABLE [Purchasing].[ProductVendor] ADD
    CONSTRAINT [FK_ProductVendor_Product_ProductID] FOREIGN KEY
    (
        [ProductID]
    ) REFERENCES [Production].[Product](
        [ProductID]
    ),
    CONSTRAINT [FK_ProductVendor_UnitMeasure_UnitMeasureCode] FOREIGN KEY
    (
        [UnitMeasureCode]
    ) REFERENCES [Production].[UnitMeasure](
        [UnitMeasureCode]
    ),
    CONSTRAINT [FK_ProductVendor_Vendor_BusinessEntityID] FOREIGN KEY
    (
        [BusinessEntityID]
    ) REFERENCES [Purchasing].[Vendor](
        [BusinessEntityID]
    );
GO

ALTER TABLE [Purchasing].[PurchaseOrderDetail] ADD
    CONSTRAINT [FK_PurchaseOrderDetail_Product_ProductID] FOREIGN KEY
    (
        [ProductID]
    ) REFERENCES [Production].[Product](
        [ProductID]
    ),
    CONSTRAINT [FK_PurchaseOrderDetail_PurchaseOrderHeader_PurchaseOrderID] FOREIGN KEY
    (
        [PurchaseOrderID]
    ) REFERENCES [Purchasing].[PurchaseOrderHeader](
        [PurchaseOrderID]
    );
GO

ALTER TABLE [Purchasing].[PurchaseOrderHeader] ADD
    CONSTRAINT [FK_PurchaseOrderHeader_Employee_EmployeeID] FOREIGN KEY
    (
        [EmployeeID]
    ) REFERENCES [HumanResources].[Employee](
        [BusinessEntityID]
    ),
    CONSTRAINT [FK_PurchaseOrderHeader_Vendor_VendorID] FOREIGN KEY
    (
        [VendorID]
    ) REFERENCES [Purchasing].[Vendor](
        [BusinessEntityID]
    ),
    CONSTRAINT [FK_PurchaseOrderHeader_ShipMethod_ShipMethodID] FOREIGN KEY
    (
        [ShipMethodID]
    ) REFERENCES [Purchasing].[ShipMethod](
        [ShipMethodID]
    );
GO

ALTER TABLE [Sales].[SalesOrderDetail] ADD
    CONSTRAINT [FK_SalesOrderDetail_SalesOrderHeader_SalesOrderID] FOREIGN KEY
    (
        [SalesOrderID]
    ) REFERENCES [Sales].[SalesOrderHeader](
        [SalesOrderID]
    ) ON DELETE CASCADE,
    CONSTRAINT [FK_SalesOrderDetail_SpecialOfferProduct_SpecialOfferIDProductID] FOREIGN KEY
    (
        [SpecialOfferID],
        [ProductID]
    ) REFERENCES [Sales].[SpecialOfferProduct](
        [SpecialOfferID],
        [ProductID]
    );
GO

ALTER TABLE [Sales].[SalesOrderHeader] ADD
    CONSTRAINT [FK_SalesOrderHeader_Address_BillToAddressID] FOREIGN KEY
    (
        [BillToAddressID]
    ) REFERENCES [Person].[Address](
        [AddressID]
    ),
    CONSTRAINT [FK_SalesOrderHeader_Address_ShipToAddressID] FOREIGN KEY
    (
        [ShipToAddressID]
    ) REFERENCES [Person].[Address](
        [AddressID]
    ),
    CONSTRAINT [FK_SalesOrderHeader_CreditCard_CreditCardID] FOREIGN KEY
    (
        [CreditCardID]
    ) REFERENCES [Sales].[CreditCard](
        [CreditCardID]
    ),
    CONSTRAINT [FK_SalesOrderHeader_CurrencyRate_CurrencyRateID] FOREIGN KEY
    (
        [CurrencyRateID]
    ) REFERENCES [Sales].[CurrencyRate](
        [CurrencyRateID]
    ),
    CONSTRAINT [FK_SalesOrderHeader_Customer_CustomerID] FOREIGN KEY
    (
        [CustomerID]
    ) REFERENCES [Sales].[Customer](
        [CustomerID]
    ),
    CONSTRAINT [FK_SalesOrderHeader_SalesPerson_SalesPersonID] FOREIGN KEY
    (
        [SalesPersonID]
    ) REFERENCES [Sales].[SalesPerson](
        [BusinessEntityID]
    ),
    CONSTRAINT [FK_SalesOrderHeader_ShipMethod_ShipMethodID] FOREIGN KEY
    (
        [ShipMethodID]
    ) REFERENCES [Purchasing].[ShipMethod](
        [ShipMethodID]
    ),
    CONSTRAINT [FK_SalesOrderHeader_SalesTerritory_TerritoryID] FOREIGN KEY
    (
        [TerritoryID]
    ) REFERENCES [Sales].[SalesTerritory](
        [TerritoryID]
    );
GO

ALTER TABLE [Sales].[SalesOrderHeaderSalesReason] ADD
    CONSTRAINT [FK_SalesOrderHeaderSalesReason_SalesReason_SalesReasonID] FOREIGN KEY
    (
        [SalesReasonID]
    ) REFERENCES [Sales].[SalesReason](
        [SalesReasonID]
    ),
    CONSTRAINT [FK_SalesOrderHeaderSalesReason_SalesOrderHeader_SalesOrderID] FOREIGN KEY
    (
        [SalesOrderID]
    ) REFERENCES [Sales].[SalesOrderHeader](
        [SalesOrderID]
    ) ON DELETE CASCADE;
GO

ALTER TABLE [Sales].[SalesPerson] ADD
    CONSTRAINT [FK_SalesPerson_Employee_BusinessEntityID] FOREIGN KEY
    (
        [BusinessEntityID]
    ) REFERENCES [HumanResources].[Employee](
        [BusinessEntityID]
    ),
    CONSTRAINT [FK_SalesPerson_SalesTerritory_TerritoryID] FOREIGN KEY
    (
        [TerritoryID]
    ) REFERENCES [Sales].[SalesTerritory](
        [TerritoryID]
    );
GO

ALTER TABLE [Sales].[SalesPersonQuotaHistory] ADD
    CONSTRAINT [FK_SalesPersonQuotaHistory_SalesPerson_BusinessEntityID] FOREIGN KEY
    (
        [BusinessEntityID]
    ) REFERENCES [Sales].[SalesPerson](
        [BusinessEntityID]
    );
GO

ALTER TABLE [Sales].[SalesTaxRate] ADD
    CONSTRAINT [FK_SalesTaxRate_StateProvince_StateProvinceID] FOREIGN KEY
    (
        [StateProvinceID]
    ) REFERENCES [Person].[StateProvince](
        [StateProvinceID]
    );
GO

ALTER TABLE [Sales].[SalesTerritory] ADD
	CONSTRAINT [FK_SalesTerritory_CountryRegion_CountryRegionCode] FOREIGN KEY
	(
		[CountryRegionCode]
	) REFERENCES [Person].[CountryRegion] (
		[CountryRegionCode]
    );
GO

ALTER TABLE [Sales].[SalesTerritoryHistory] ADD
    CONSTRAINT [FK_SalesTerritoryHistory_SalesPerson_BusinessEntityID] FOREIGN KEY
    (
        [BusinessEntityID]
    ) REFERENCES [Sales].[SalesPerson](
        [BusinessEntityID]
    ),
    CONSTRAINT [FK_SalesTerritoryHistory_SalesTerritory_TerritoryID] FOREIGN KEY
    (
        [TerritoryID]
    ) REFERENCES [Sales].[SalesTerritory](
        [TerritoryID]
    );
GO

ALTER TABLE [Sales].[ShoppingCartItem] ADD
    CONSTRAINT [FK_ShoppingCartItem_Product_ProductID] FOREIGN KEY
    (
        [ProductID]
    ) REFERENCES [Production].[Product](
        [ProductID]
    );
GO

ALTER TABLE [Sales].[SpecialOfferProduct] ADD
    CONSTRAINT [FK_SpecialOfferProduct_Product_ProductID] FOREIGN KEY
    (
        [ProductID]
    ) REFERENCES [Production].[Product](
        [ProductID]
    ),
    CONSTRAINT [FK_SpecialOfferProduct_SpecialOffer_SpecialOfferID] FOREIGN KEY
    (
        [SpecialOfferID]
    ) REFERENCES [Sales].[SpecialOffer](
        [SpecialOfferID]
    );
GO

ALTER TABLE [Person].[StateProvince] ADD
    CONSTRAINT [FK_StateProvince_CountryRegion_CountryRegionCode] FOREIGN KEY
    (
        [CountryRegionCode]
    ) REFERENCES [Person].[CountryRegion](
        [CountryRegionCode]
    ),
    CONSTRAINT [FK_StateProvince_SalesTerritory_TerritoryID] FOREIGN KEY
    (
        [TerritoryID]
    ) REFERENCES [Sales].[SalesTerritory](
        [TerritoryID]
    );
GO

ALTER TABLE [Sales].[Store] ADD
	CONSTRAINT [FK_Store_BusinessEntity_BusinessEntityID] FOREIGN KEY
	(
		[BusinessEntityID]
	) REFERENCES [Person].[BusinessEntity](
		[BusinessEntityID]
	),
    CONSTRAINT [FK_Store_SalesPerson_SalesPersonID] FOREIGN KEY
    (
        [SalesPersonID]
    ) REFERENCES [Sales].[SalesPerson](
        [BusinessEntityID]
    );
GO

ALTER TABLE [Production].[TransactionHistory] ADD
    CONSTRAINT [FK_TransactionHistory_Product_ProductID] FOREIGN KEY
    (
        [ProductID]
    ) REFERENCES [Production].[Product](
        [ProductID]
    );
GO

ALTER TABLE [Purchasing].[Vendor] ADD
	CONSTRAINT [FK_Vendor_BusinessEntity_BusinessEntityID] FOREIGN KEY
	(
		[BusinessEntityID]
	) REFERENCES [Person].[BusinessEntity](
		[BusinessEntityID]
	);
GO

ALTER TABLE [Production].[WorkOrder] ADD
    CONSTRAINT [FK_WorkOrder_Product_ProductID] FOREIGN KEY
    (
        [ProductID]
    ) REFERENCES [Production].[Product](
        [ProductID]
    ),
    CONSTRAINT [FK_WorkOrder_ScrapReason_ScrapReasonID] FOREIGN KEY
    (
        [ScrapReasonID]
    ) REFERENCES [Production].[ScrapReason](
        [ScrapReasonID]
    );
GO

ALTER TABLE [Production].[WorkOrderRouting] ADD
    CONSTRAINT [FK_WorkOrderRouting_Location_LocationID] FOREIGN KEY
    (
        [LocationID]
    ) REFERENCES [Production].[Location](
        [LocationID]
    ),
    CONSTRAINT [FK_WorkOrderRouting_WorkOrder_WorkOrderID] FOREIGN KEY
    (
        [WorkOrderID]
    ) REFERENCES [Production].[WorkOrder](
        [WorkOrderID]
    );
GO

