type SearchValue = number | string | null | undefined;

export interface VenueSearchIndex {
  idValues: string[];
  textValue: string;
}

export interface VenueSearchInput {
  ids?: SearchValue[];
  texts?: SearchValue[];
}

export function createVenueSearchIndex(input: VenueSearchInput): VenueSearchIndex {
  const idValues = uniqueValues((input.ids ?? []).map(normalizeSearchValue).filter(Boolean));
  const textValues = uniqueValues((input.texts ?? []).map(normalizeSearchValue).filter(Boolean));
  const compactTextValues = textValues
    .map((value) => value.replace(/\s+/g, ""))
    .filter((value) => value.length > 0);

  return {
    idValues,
    textValue: uniqueValues([...textValues, ...compactTextValues]).join(" "),
  };
}

export function matchesVenueSearch(index: VenueSearchIndex, query: string) {
  const normalizedQuery = normalizeSearchValue(query);
  if (!normalizedQuery) return true;

  return normalizedQuery.split(" ").every((token) => {
    const compactToken = token.replace(/\s+/g, "");
    return (
      index.idValues.includes(token) ||
      index.textValue.includes(token) ||
      (compactToken !== token && index.textValue.includes(compactToken))
    );
  });
}

export function formatVenueLocation(building?: string | null, roomNo?: string | null) {
  return [building, roomNo].filter(Boolean).join(" / ");
}

function normalizeSearchValue(value: SearchValue) {
  return String(value ?? "")
    .normalize("NFKC")
    .trim()
    .toLowerCase()
    .replace(/\s+/g, " ");
}

function uniqueValues(values: string[]) {
  return Array.from(new Set(values));
}
