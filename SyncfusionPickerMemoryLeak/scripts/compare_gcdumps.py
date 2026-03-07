#!/usr/bin/env python3
"""Compare two gcdump text reports and show what grew."""

import re
import sys
from collections import defaultdict

def parse_gcdump(path):
    """Parse a gcdump text file into {type_name: (bytes, count)} dict."""
    types = {}
    header = {}
    with open(path, 'r', encoding='utf-8') as f:
        for line in f:
            line = line.strip()
            # Header lines
            m = re.match(r'^([\d,]+)\s+GC Heap bytes', line)
            if m:
                header['heap_bytes'] = int(m.group(1).replace(',', ''))
                continue
            m = re.match(r'^([\d,]+)\s+GC Heap objects', line)
            if m:
                header['heap_objects'] = int(m.group(1).replace(',', ''))
                continue
            # Data lines: bytes count type
            m = re.match(r'^([\d,]+)\s+([\d,]+)\s+(.+?)(?:\s+\(Bytes > [\d]+K\))?\s+\[', line)
            if m:
                obj_bytes = int(m.group(1).replace(',', ''))
                count = int(m.group(2).replace(',', ''))
                type_name = m.group(3).strip()
                # Aggregate if same type appears multiple times (different size buckets)
                if type_name in types:
                    old_b, old_c = types[type_name]
                    types[type_name] = (old_b + obj_bytes, old_c + count)
                else:
                    types[type_name] = (obj_bytes, count)
    return header, types

def main():
    if len(sys.argv) != 3:
        print("Usage: compare_gcdumps.py <baseline_gcdump> <last_gcdump>", file=sys.stderr)
        sys.exit(2)

    base_path = sys.argv[1]
    last_path = sys.argv[2]

    base_hdr, base_types = parse_gcdump(base_path)
    last_hdr, last_types = parse_gcdump(last_path)

    print("=" * 90)
    print("GC DUMP COMPARISON: Baseline vs After 10 Nav Cycles")
    print("=" * 90)
    print(f"{'':30s} {'Baseline':>15s} {'After':>15s} {'Delta':>15s}")
    print(f"{'Heap Bytes':30s} {base_hdr['heap_bytes']:>15,d} {last_hdr['heap_bytes']:>15,d} {last_hdr['heap_bytes']-base_hdr['heap_bytes']:>+15,d}")
    print(f"{'Heap Objects':30s} {base_hdr['heap_objects']:>15,d} {last_hdr['heap_objects']:>15,d} {last_hdr['heap_objects']-base_hdr['heap_objects']:>+15,d}")
    print()

    # Compute deltas
    all_types = set(base_types.keys()) | set(last_types.keys())
    deltas = []
    for t in all_types:
        b_bytes, b_count = base_types.get(t, (0, 0))
        l_bytes, l_count = last_types.get(t, (0, 0))
        d_bytes = l_bytes - b_bytes
        d_count = l_count - b_count
        if d_count != 0 or d_bytes != 0:
            deltas.append((d_bytes, d_count, b_bytes, b_count, l_bytes, l_count, t))

    # Sort by count delta descending (most leaked objects first)
    deltas.sort(key=lambda x: x[1], reverse=True)

    print("=" * 90)
    print("TOP GROWERS BY OBJECT COUNT")
    print("=" * 90)
    print(f"{'Delta Count':>12s} {'Delta Bytes':>12s} {'Base Count':>11s} {'Last Count':>11s}  Type")
    print("-" * 90)
    for d_bytes, d_count, b_bytes, b_count, l_bytes, l_count, t in deltas[:60]:
        if d_count > 0:
            print(f"{d_count:>+12,d} {d_bytes:>+12,d} {b_count:>11,d} {l_count:>11,d}  {t}")

    print()
    print("=" * 90)
    print("TOP GROWERS BY BYTES")
    print("=" * 90)
    deltas.sort(key=lambda x: x[0], reverse=True)
    print(f"{'Delta Bytes':>12s} {'Delta Count':>12s} {'Base Bytes':>11s} {'Last Bytes':>11s}  Type")
    print("-" * 90)
    for d_bytes, d_count, b_bytes, b_count, l_bytes, l_count, t in deltas[:40]:
        if d_bytes > 0:
            print(f"{d_bytes:>+12,d} {d_count:>+12,d} {b_bytes:>11,d} {l_bytes:>11,d}  {t}")

    print()
    print("=" * 90)
    print("NEW TYPES (not in baseline)")
    print("=" * 90)
    new_types = [(l_bytes, l_count, t) for t in last_types if t not in base_types for l_bytes, l_count in [last_types[t]]]
    new_types.sort(key=lambda x: x[1], reverse=True)
    print(f"{'Bytes':>12s} {'Count':>8s}  Type")
    print("-" * 90)
    for b, c, t in new_types[:40]:
        print(f"{b:>12,d} {c:>8,d}  {t}")

    # App-specific types summary
    print()
    print("=" * 90)
    print("APP-SPECIFIC TYPE DELTAS (SyncfusionPickerMemoryLeak.*)")
    print("=" * 90)
    app_deltas = [(d_bytes, d_count, b_bytes, b_count, l_bytes, l_count, t)
                  for d_bytes, d_count, b_bytes, b_count, l_bytes, l_count, t in deltas
                  if 'syncfusionpickermemoryleak' in t.lower() and (d_count != 0 or d_bytes != 0)]
    app_deltas.sort(key=lambda x: x[1], reverse=True)
    print(f"{'Delta Count':>12s} {'Delta Bytes':>12s} {'Base Count':>11s} {'Last Count':>11s}  Type")
    print("-" * 90)
    for d_bytes, d_count, b_bytes, b_count, l_bytes, l_count, t in app_deltas:
        print(f"{d_count:>+12,d} {d_bytes:>+12,d} {b_count:>11,d} {l_count:>11,d}  {t}")

    # MAUI controls summary
    print()
    print("=" * 90)
    print("MAUI/SYNCFUSION CONTROL DELTAS")
    print("=" * 90)
    ui_deltas = [(d_bytes, d_count, b_bytes, b_count, l_bytes, l_count, t)
                 for d_bytes, d_count, b_bytes, b_count, l_bytes, l_count, t in deltas
                 if ('Microsoft.Maui.Controls' in t or 'Syncfusion' in t) and d_count > 0
                 and not t.startswith('System.Collections')]
    ui_deltas.sort(key=lambda x: x[1], reverse=True)
    print(f"{'Delta Count':>12s} {'Base Count':>11s} {'Last Count':>11s}  Type")
    print("-" * 90)
    for d_bytes, d_count, b_bytes, b_count, l_bytes, l_count, t in ui_deltas:
        print(f"{d_count:>+12,d} {b_count:>11,d} {l_count:>11,d}  {t}")

if __name__ == '__main__':
    main()
