export function createJobSubmission(send) {
  let previous, key;
  return async (payload) => {
    const fingerprint = JSON.stringify(payload);
    if (fingerprint !== previous) {
      previous = fingerprint;
      key = crypto.randomUUID();
    }
    const result = await send({ ...payload, idempotencyKey: key });
    previous = undefined;
    key = undefined;
    return result;
  };
}
export function updateJob(items, update) {
  return items.map((item) =>
    item.id === update.id ? mergeJob(item, update) : item,
  );
}
export function mergeJob(current, update) {
  return !current || (current.id === update.id && update.version >= current.version)
    ? { ...current, ...update }
    : current;
}
