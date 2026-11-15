import { keepPreviousData, useMutation, useQuery } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { api, unwrap } from "../api/client";
import { FRONT_DESK, useAuth } from "../auth/AuthContext";
import { Card, Empty, ErrorMessage, Field, Loading, PageHeader } from "../components/ui";

const PAGE_SIZE = 20;

export function CustomersPage() {
  const { hasRole } = useAuth();
  const [params, setParams] = useSearchParams();
  const search = params.get("search") ?? "";
  const page = Number(params.get("page") ?? "1");
  const [text, setText] = useState(search);
  const [adding, setAdding] = useState(false);

  const customers = useQuery({
    queryKey: ["customers", search, page],
    queryFn: async () =>
      unwrap(await api.GET("/api/customers", { params: { query: { search: search || undefined, page, pageSize: PAGE_SIZE } } })),
    placeholderData: keepPreviousData,
  });

  const submitSearch = (event: FormEvent) => {
    event.preventDefault();
    setParams(text.trim() ? { search: text.trim() } : {});
  };

  return (
    <>
      <PageHeader
        title="Customers"
        subtitle={customers.data ? `${customers.data.totalCount} customers` : undefined}
        actions={
          hasRole(...FRONT_DESK) && (
            <button type="button" className="primary" onClick={() => setAdding(true)}>
              New customer
            </button>
          )
        }
      />
      {adding && <NewCustomer onClose={() => setAdding(false)} />}
      <form className="toolbar" onSubmit={submitSearch} role="search">
        <input
          type="search"
          placeholder="Search by name, e-mail or phone"
          value={text}
          onChange={(e) => setText(e.target.value)}
          aria-label="Search customers"
        />
        <button type="submit">Search</button>
      </form>
      <ErrorMessage error={customers.error} />
      {customers.isPending && <Loading />}
      {customers.data && (
        <Card>
          {customers.data.items.length === 0 ? (
            <Empty>No customers found.</Empty>
          ) : (
            <table>
              <thead>
                <tr>
                  <th>Name</th>
                  <th>E-mail</th>
                  <th>Phone</th>
                </tr>
              </thead>
              <tbody>
                {customers.data.items.map((customer) => (
                  <tr key={customer.id}>
                    <td>
                      <Link to={`/customers/${customer.id}`}>
                        {customer.lastName}, {customer.firstName}
                      </Link>
                    </td>
                    <td>{customer.email}</td>
                    <td>{customer.phoneNumber ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          {(customers.data.totalPages ?? 1) > 1 && (
            <div className="pager">
              <button type="button" disabled={page <= 1} onClick={() => setParams({ search, page: String(page - 1) })}>
                Previous
              </button>
              <span className="muted">
                Page {page} of {(customers.data.totalPages ?? 1)}
              </span>
              <button
                type="button"
                disabled={page >= (customers.data.totalPages ?? 1)}
                onClick={() => setParams({ search, page: String(page + 1) })}
              >
                Next
              </button>
            </div>
          )}
        </Card>
      )}
    </>
  );
}

function NewCustomer({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate();
  const [form, setForm] = useState({ firstName: "", lastName: "", email: "", phoneNumber: "" });
  const create = useMutation({
    mutationFn: async () =>
      unwrap(await api.POST("/api/customers", { body: { ...form, phoneNumber: form.phoneNumber.trim() || null } })),
    onSuccess: (customer) => navigate(`/customers/${customer.id}`),
  });
  const set = (key: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [key]: e.target.value });

  return (
    <Card title="New customer">
      <form
        className="form-grid"
        onSubmit={(e) => {
          e.preventDefault();
          create.mutate();
        }}
      >
        <Field label="First name">
          <input required value={form.firstName} onChange={set("firstName")} />
        </Field>
        <Field label="Last name">
          <input required value={form.lastName} onChange={set("lastName")} />
        </Field>
        <Field label="E-mail">
          <input type="email" required value={form.email} onChange={set("email")} />
        </Field>
        <Field label="Phone">
          <input value={form.phoneNumber} onChange={set("phoneNumber")} />
        </Field>
        <div className="actions">
          <button type="submit" className="primary" disabled={create.isPending}>
            Save customer
          </button>
          <button type="button" onClick={onClose}>
            Cancel
          </button>
        </div>
      </form>
      <ErrorMessage error={create.error} />
    </Card>
  );
}
