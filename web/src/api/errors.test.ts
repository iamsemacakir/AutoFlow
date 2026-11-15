import { ApiError, messageOf } from "./errors";

describe("ApiError.from", () => {
  it("prefers the first field error of a validation problem", () => {
    const error = ApiError.from(400, {
      title: "Invalid request",
      detail: "One or more fields are missing or invalid.",
      code: "validation.failed",
      errors: { email: ["'email' is required."] },
    });

    expect(error.code).toBe("validation.failed");
    expect(error.message).toBe("'email' is required.");
    expect(error.fieldErrors).toEqual({ email: ["'email' is required."] });
  });

  it("uses the detail of other problems", () => {
    const error = ApiError.from(409, { title: "Conflict", detail: "Bay 'Lift 1' is out of service.", code: "appointment.bay_out_of_service" });

    expect(error.status).toBe(409);
    expect(error.message).toBe("Bay 'Lift 1' is out of service.");
  });

  it("copes with answers that are not problems", () => {
    const error = ApiError.from(502, "Bad Gateway");

    expect(error.code).toBe("http.502");
    expect(messageOf(error)).toBe("Request failed (502).");
  });
});
