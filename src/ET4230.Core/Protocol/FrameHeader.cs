using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;

namespace ET4230.Core.Protocol;

public readonly record struct FrameHeader(
    byte Version,
    byte Type,
    ushort Flags,
    uint RequestId,
    uint PayloadLength);

