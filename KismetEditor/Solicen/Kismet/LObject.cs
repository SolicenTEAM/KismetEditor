﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UAssetAPI.Kismet.Bytecode;

namespace Solicen.Kismet
{
    /// <summary>
    /// Представляет собой простую Lite структуру данных (DTO) для хранения
    /// извлеченной информации об одной инструкции Kismet.
    /// </summary>
    internal class LObject
    {
        public int StatementIndex { get; }
        public string Value { get; }
        public string Instruction { get; }
        public int Offset { get; }
        public int InstructionSize { get; }

        public LObject(int statementIndex, string value, string instruction, int offset, int instructionSize)
        {
            StatementIndex = statementIndex;
            Value = value;
            Instruction = instruction;
            Offset = offset;
            InstructionSize = instructionSize;
        }
    }
}
