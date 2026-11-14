using System;
using System.Collections.Generic;
using System.Text;

namespace ET4230.Core.Protocol;

public enum HeaderDecodeError
{
    None, // No error
    InvalidHeaderSize, // The header size is not as expected
    InvalidMagic, // The magic number in the header is incorrect
    UnsupportedVersion, // The version specified in the header is not supported
    UnknownType, // The type specified in the header is unknown
    InvalidFlags, // The flags specified in the header are invalid
    InvalidRequestId, // The request ID specified in the header is invalid
    InvalidPayloadLength // The payload length specified in the header is invalid
}

