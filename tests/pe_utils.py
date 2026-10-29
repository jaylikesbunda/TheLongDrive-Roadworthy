"""Minimal, read-only PE header and resource checks for the shipped EXE."""
from __future__ import annotations

import struct

RT_ICON = 3
RT_GROUP_ICON = 14


def _u16(data, offset):
    return struct.unpack_from("<H", data, offset)[0]


def _u32(data, offset):
    return struct.unpack_from("<I", data, offset)[0]


def sections(data):
    if data[:2] != b"MZ":
        raise ValueError("Not a PE file.")
    pe = _u32(data, 0x3C)
    if data[pe:pe + 4] != b"PE\0\0":
        raise ValueError("Missing PE signature.")
    count = _u16(data, pe + 6)
    optional_size = _u16(data, pe + 20)
    table = pe + 24 + optional_size
    result = []
    for index in range(count):
        header = table + 40 * index
        result.append({
            "name": data[header:header + 8].rstrip(b"\0").decode("ascii", "replace"),
            "virtual_size": _u32(data, header + 8),
            "virtual_address": _u32(data, header + 12),
            "raw_size": _u32(data, header + 16),
            "raw_offset": _u32(data, header + 20),
        })
    return result


def subsystem(data):
    """IMAGE_OPTIONAL_HEADER.Subsystem: 2 = WINDOWS_GUI, 3 = CONSOLE."""
    pe = _u32(data, 0x3C)
    return _u16(data, pe + 24 + 68)


def resource_types(data):
    """Set of resource type ids (RT_ICON, RT_GROUP_ICON, ...) in .rsrc."""
    rsrc = next((item for item in sections(data) if item["name"] == ".rsrc"), None)
    if rsrc is None:
        return set()
    base = rsrc["raw_offset"]
    found = set()
    named = _u16(data, base + 12)
    ids = _u16(data, base + 14)
    for index in range(named + ids):
        entry = base + 16 + 8 * index
        name_or_id = _u32(data, entry)
        offset = _u32(data, entry + 4)
        if not offset & 0x80000000:
            continue
        if not name_or_id & 0x80000000:
            found.add(name_or_id)
    return found
