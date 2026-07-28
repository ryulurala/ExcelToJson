import { getPrettyValue, parseArrayValue } from "./utils.js";

export function compileTable(sheetData, startRow, keyIndex, type) {
  const value = type === "[{}]" ? [] : {};
  let row = startRow;
  const keyCol = keyIndex["$key"];

  if (type === "{[]}") {
    for (let subkey in keyIndex) {
      let isArray = subkey.endsWith("[]");
      let cleanKey = isArray ? subkey.slice(0, -2) : subkey;
      const colIdx = keyIndex[subkey];
      const objArray = [];
      let r = startRow;
      while (sheetData[r] && sheetData[r][colIdx] !== undefined && sheetData[r][colIdx] !== "") {
        const cellValue = sheetData[r][colIdx];
        objArray.push(isArray ? parseArrayValue(cellValue) : getPrettyValue(cellValue));
        r++;
      }
      value[cleanKey] = objArray;
    }
    return value;
  }

  while (sheetData[row] && (type === "[{}]" || sheetData[row][keyCol])) {
    const key = type === "[{}]" ? null : sheetData[row][keyCol];
    let obj = null;

    if (type === "{}") {
      let valCol = keyIndex["$value"] ?? keyIndex["$value[]"];
      if (valCol !== undefined) {
        obj =
          keyIndex["$value[]"] !== undefined
            ? parseArrayValue(sheetData[row][valCol])
            : getPrettyValue(sheetData[row][valCol]);
      }
    } else {
      obj = {};
      let isSane = false;
      for (let subkey in keyIndex) {
        if (subkey === "$key" && type !== "[{}]") continue;
        const colIdx = keyIndex[subkey];
        const cellValue = sheetData[row][colIdx];
        if (subkey.endsWith("[]")) {
          const cleanKey = subkey.slice(0, -2);
          obj[cleanKey] = parseArrayValue(cellValue);
          if (obj[cleanKey].length > 0) isSane = true;
        } else {
          obj[subkey] = getPrettyValue(cellValue);
          if (obj[subkey] !== "") isSane = true;
        }
      }
      if (type === "[{}]" && !isSane) break;
    }

    if (type === "[{}]") value.push(obj);
    else if (type === "{{}}" || type === "{}") value[key] = obj;

    row++;
  }
  return value;
}
