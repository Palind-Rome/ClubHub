export interface PrepareLearningDownloadOptions {
  itemId: number;
  token: string;
  confirmDownload: () => Promise<unknown>;
  fetchFile?: typeof fetch;
  createObjectUrl?: (file: Blob) => string;
}

export async function prepareLearningDownload({
  itemId,
  token,
  confirmDownload,
  fetchFile = fetch,
  createObjectUrl = URL.createObjectURL,
}: PrepareLearningDownloadOptions): Promise<string> {
  const response = await fetchFile(`/api/v1/learning/items/${itemId}/file?download=true`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!response.ok) throw new Error("文件内容获取失败");

  const file = await response.blob();
  await confirmDownload();
  return createObjectUrl(file);
}
