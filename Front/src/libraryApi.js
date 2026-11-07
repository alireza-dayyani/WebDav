export function libraryPath(page, trash, query) {
  return `/library?page=${page}&trash=${trash}&q=${encodeURIComponent(query)}`;
}
