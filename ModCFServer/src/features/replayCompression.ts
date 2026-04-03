function toUint8Array(value: ArrayBuffer | Uint8Array): Uint8Array {
  return value instanceof Uint8Array ? value : new Uint8Array(value);
}

async function readStreamBytes(stream: ReadableStream<Uint8Array>): Promise<Uint8Array> {
  return new Uint8Array(await new Response(stream).arrayBuffer());
}

export async function gzipBytes(
  value: ArrayBuffer | Uint8Array,
): Promise<Uint8Array> {
  const compressionStream = new CompressionStream("gzip");
  const writer = compressionStream.writable.getWriter();
  await writer.write(toUint8Array(value));
  await writer.close();
  return readStreamBytes(compressionStream.readable);
}

export async function gunzipBytes(
  value: ArrayBuffer | Uint8Array,
): Promise<Uint8Array> {
  const decompressionStream = new DecompressionStream("gzip");
  const writer = decompressionStream.writable.getWriter();
  await writer.write(toUint8Array(value));
  await writer.close();
  return readStreamBytes(decompressionStream.readable);
}
