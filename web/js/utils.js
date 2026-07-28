export function getPrettyValue(value) {
  if (value === null || value === undefined || value === "") return "";
  if (typeof value === "number") return value;
  const strVal = String(value).trim();
  const strWithoutComma = strVal.replace(/,/g, "");
  if (strVal !== "" && !isNaN(Number(strWithoutComma))) return Number(strWithoutComma);
  return strVal;
}

export function parseArrayValue(csvString) {
  if (!csvString) return [];
  return String(csvString)
    .split(",")
    .map((item) => getPrettyValue(item));
}

export function downloadBlob(filename, dataBlob, mimeType) {
  const blob = typeof dataBlob === "string" ? new Blob([dataBlob], { type: mimeType }) : dataBlob;
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}
