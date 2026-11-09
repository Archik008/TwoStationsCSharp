from configs import SEGMENT_ORDER, segment_groups, segment_to_block




def parse_arduino_string(line, seg_occ_dict, diag_occ_dict):
    if "Data: " not in line:
        return

    raw_bin = line.replace("Data: ", "").strip()

    # 1. Добиваем нулями до 24 бит
    # 2. [[::-1]] — РАЗВОРАЧИВАЕМ строку задом наперед
    bin_str = raw_bin.zfill(24)[::-1]

    for idx, char in enumerate(bin_str):
        if idx >= len(SEGMENT_ORDER):
            break

        seg = SEGMENT_ORDER[idx]
        if seg == "EMPTY" or not isinstance(seg, tuple):
            continue

        # Теперь бит №0 будет в самом конце строки от Arduino
        is_occupied = (char == '0')

        if is_occupied:

            print(f"Сработал бит №{idx} для сегмента {seg}")


        block = segment_to_block.get(seg)
        if block:
            for s in segment_groups[block]:
                if s['type'] == "segment":
                    seg_occ_dict[s['id']] = 0 if is_occupied else 1
                elif s["type"] == "diag":
                    diag_occ_dict[s['name']] = 0 if is_occupied else 1
        else:
            seg_occ_dict[seg] = 0 if is_occupied else 1

        #seg_occ_dict[seg] = 0 if is_occupied else 1