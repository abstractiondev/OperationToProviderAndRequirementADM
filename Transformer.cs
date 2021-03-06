﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using System.Xml.Serialization;
using OP=Operation_v1_0;
using Operation_v1_0;
using ECO=ProviderAndRequirement_v1_0;
using ProviderAndRequirement_v1_0;
using LogicalInformationType = Operation_v1_0.LogicalInformationType;

namespace OperationToProviderAndRequirementTRANS
{
    public class Transformer
    {
        T LoadXml<T>(string xmlFileName)
        {
            using (FileStream fStream = File.OpenRead(xmlFileName))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(T));
                T result = (T)serializer.Deserialize(fStream);
                fStream.Close();
                return result;
            }
        }

	    public Tuple<string, string>[] GetGeneratorContent(params string[] xmlFileNames)
	    {
            List<Tuple<string, string>> result = new List<Tuple<string, string>>();
            foreach(string xmlFileName in xmlFileNames)
            {
                OperationAbstractionType fromAbs = LoadXml<OperationAbstractionType>(xmlFileName);
                CurrSource = fromAbs;
                CurrGitId = Path.GetFileNameWithoutExtension(xmlFileName);
                ProviderAndRequirementECOType toAbs = TransformAbstraction(fromAbs);
                string xmlContent = WriteToXmlString(toAbs);
                FileInfo fInfo = new FileInfo(xmlFileName);
                string contentFileName = "ProviderAndRequirementFrom" + fInfo.Name;
                result.Add(Tuple.Create(contentFileName, xmlContent));
            }
	        return result.ToArray();
	    }

        private string WriteToXmlString(ProviderAndRequirementECOType toAbs)
        {
            XmlSerializer serializer = new XmlSerializer(toAbs.GetType());
            MemoryStream memoryStream = new MemoryStream();
            serializer.Serialize(memoryStream, toAbs);
            byte[] data = memoryStream.ToArray();
            string result = System.Text.Encoding.UTF8.GetString(data);
            return result;
        }

        public static OperationAbstractionType CurrSource;
        public static string CurrGitId = null;

        public static ProviderAndRequirementECOType TransformAbstraction(OperationAbstractionType fromAbs)
        {
            CurrSource = fromAbs;
            ProviderAndRequirementECOType toAbs = new ProviderAndRequirementECOType
                                                       {
                                                           //gitId = ConvertHexStringToByteArray(CurrGitId),
                                                           Provides = GetProvidesData(fromAbs),
                                                           Requires = GetRequiresData(fromAbs),
                                                       };
            return toAbs;
        }

        private static ContractType GetRequiresData(OperationAbstractionType fromAbs)
        {
            ContractType contract = new ContractType
                                            {
                                                Behaviors = GetRequiresOperations(fromAbs.Operations.Operation)
                                            };
            if (contract.Behaviors == null || contract.Behaviors.Length == 0)
                contract.Behaviors = null;
            return contract;
        }

        private static LogicalOperationSignatureType[] GetRequiresOperations(OperationType[] operations)
        {
            var result =
                operations.SelectMany(operation => operation.Execution.SequentialExecution).Select(
                    seqExec => seqExec as OperationExecuteType)
                    .Where(opExec => opExec != null && opExec.OperationSignature != null).Select(
                        opExec => opExec.OperationSignature)
                    .Select(ConvertOperationSignature).ToArray();
            return result;
        }

        private static LogicalOperationSignatureType ConvertOperationSignature(OperationSignatureType opSignature)
        {
            var result = new LogicalOperationSignatureType
                       {
                           logicalNamespace = opSignature.logicalNamespace,
                           name = opSignature.name,
                           Parameter =
                               opSignature.Parameter == null
                                   ? null
                                   : opSignature.Parameter.Select(parameter => ConvertLogicalInformationType(parameter)).ToArray(),
                           ReturnValue = opSignature.ReturnValue == null ? null : ConvertLogicalInformationType(opSignature.ReturnValue),
                       };
            CalculateHashData(result);
            return result;
        }

        private static void CalculateHashData(LogicalOperationSignatureType signature)
        {
            List<string> hashSourceData = new List<string>();
            hashSourceData.Add(signature.logicalNamespace);
            hashSourceData.Add(signature.name);
            if (signature.Parameter != null)
            {
                foreach (var parameter in signature.Parameter)
                    hashSourceData.AddRange(GetHashStringArray(parameter));
            }
            else
                hashSourceData.Add(String.Empty);
            if (signature.ReturnValue != null)
                hashSourceData.AddRange(GetHashStringArray(signature.ReturnValue));
            else
                hashSourceData.Add(String.Empty);
            signature.sha1Hash = CalculateHashFromStringArray(hashSourceData.ToArray());
        }

        private static ECO.LogicalInformationType ConvertLogicalInformationType(OP.LogicalInformationType parameter)
        {
            var result = new ECO.LogicalInformationType
                       {
                           logicalDatatype = parameter.logicalDatatype,
                           logicalNamespace = parameter.logicalNamespace,
                           name = parameter.name,
                           
                       };
            result.sha1Hash = CalculateHashFromStringArray(GetHashStringArray(result));
            return result;
        }

        private static string[] GetHashStringArray(ECO.LogicalInformationType signature)
        {
            return new string[]
                       {
                           signature.logicalDatatype, signature.logicalNamespace,
                           signature.name
                       };
        }

        private static byte[] CalculateHashFromStringArray(params string[] sourceArray)
        {
            byte[] sourceData = sourceArray.SelectMany(strItem =>
                                       {
                                           byte[] data = ASCIIEncoding.ASCII.GetBytes(strItem);
                                           List<byte> dataList = new List<byte>(data);
                                           dataList.Add(0x00);
                                           return dataList.ToArray();
                                       }).ToArray();
            SHA1 sha1 = SHA1.Create();
            byte[] hash = sha1.ComputeHash(sourceData);
            return hash;
        }

        private static ContractType GetProvidesData(OperationAbstractionType fromAbs)
        {
            ContractType contract = new ContractType
                                        {
                                            Behaviors = GetProvidesOperations(fromAbs.Operations.Operation),
                                        };

            if (contract.Behaviors == null || contract.Behaviors.Length == 0)
                contract.Behaviors = null;
            return contract;
        }

        private static LogicalOperationSignatureType[] GetProvidesOperations(OperationType[] operations)
        {
            return
                operations.Where(operation => operation.OperationSignature != null).Select(
                    operation => operation.OperationSignature)
                    .Select(ConvertOperationSignature).ToArray();
        }

        public static byte[] ConvertHexStringToByteArray(string hexString)
        {
            if (hexString.Length % 2 != 0)
            {
                throw new ArgumentException(String.Format(CultureInfo.InvariantCulture, "The binary key cannot have an odd number of digits: {0}", hexString));
            }

            byte[] hexAsBytes = new byte[hexString.Length / 2];
            for (int index = 0; index < hexAsBytes.Length; index++)
            {
                string byteValue = hexString.Substring(index * 2, 2);
                hexAsBytes[index] = byte.Parse(byteValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return hexAsBytes;
        } 

    }
}
