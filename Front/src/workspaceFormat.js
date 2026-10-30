export function formatDate(value, tag, dateOnly = false) {
  return new Intl.DateTimeFormat(tag, {
    dateStyle: "medium",
    ...(dateOnly ? {} : { timeStyle: "short" }),
  }).format(value);
}
export function formatNumber(value, tag) {
  return new Intl.NumberFormat(tag).format(value);
}
