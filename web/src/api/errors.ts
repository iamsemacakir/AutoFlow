/**
 * An error answer of the API: an RFC 9457 problem with a stable `code`
 * (see the README's error table) and, for 400 validation problems, the
 * messages per field.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly fieldErrors: Record<string, string[]>;

  constructor(status: number, code: string, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
    this.fieldErrors = fieldErrors;
  }

  /** Builds the error from a problem body; anything else becomes a generic message. */
  static from(status: number, body: unknown): ApiError {
    const problem = (typeof body === "object" && body !== null ? body : {}) as {
      code?: unknown;
      title?: unknown;
      detail?: unknown;
      errors?: unknown;
    };
    const fieldErrors = isFieldErrors(problem.errors) ? problem.errors : {};
    const candidates = [Object.values(fieldErrors)[0]?.[0], problem.detail, problem.title];
    const message = candidates.find((text): text is string => typeof text === "string" && text.length > 0);

    return new ApiError(
      status,
      typeof problem.code === "string" ? problem.code : `http.${status}`,
      message ?? `Request failed (${status}).`,
      fieldErrors,
    );
  }
}

function isFieldErrors(value: unknown): value is Record<string, string[]> {
  return (
    typeof value === "object" &&
    value !== null &&
    Object.values(value).every((messages) => Array.isArray(messages) && messages.every((m) => typeof m === "string"))
  );
}

/** A message for any error a query or mutation can throw. */
export function messageOf(error: unknown): string {
  if (error instanceof ApiError && error.status === 412) {
    return "Someone else changed this in the meantime. The page now shows their version; check it and try again.";
  }

  if (error instanceof ApiError) {
    return error.message;
  }

  return error instanceof Error ? error.message : "Something went wrong.";
}
