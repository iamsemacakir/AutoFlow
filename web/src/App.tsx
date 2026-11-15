import { Route, Routes } from "react-router-dom";
import { FRONT_DESK } from "./auth/AuthContext";
import { Layout } from "./components/Layout";
import { RequireAuth } from "./components/RequireAuth";
import { AccountPage } from "./pages/AccountPage";
import { BaysPage } from "./pages/BaysPage";
import { CustomerPage } from "./pages/CustomerPage";
import { CustomersPage } from "./pages/CustomersPage";
import { InvoicePage } from "./pages/InvoicePage";
import { InvoicesPage } from "./pages/InvoicesPage";
import { LoginPage } from "./pages/LoginPage";
import { NotFoundPage } from "./pages/NotFoundPage";
import { SchedulePage } from "./pages/SchedulePage";
import { StaffPage } from "./pages/StaffPage";
import { VehiclePage } from "./pages/VehiclePage";
import { WorkOrderPage } from "./pages/WorkOrderPage";

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        element={
          <RequireAuth>
            <Layout />
          </RequireAuth>
        }
      >
        <Route index element={<SchedulePage />} />
        <Route path="customers" element={<CustomersPage />} />
        <Route path="customers/:id" element={<CustomerPage />} />
        <Route path="vehicles/:id" element={<VehiclePage />} />
        <Route path="work-orders/:id" element={<WorkOrderPage />} />
        <Route path="bays" element={<BaysPage />} />
        <Route path="account" element={<AccountPage />} />
        <Route
          path="invoices"
          element={
            <RequireAuth roles={FRONT_DESK}>
              <InvoicesPage />
            </RequireAuth>
          }
        />
        <Route
          path="invoices/:id"
          element={
            <RequireAuth roles={FRONT_DESK}>
              <InvoicePage />
            </RequireAuth>
          }
        />
        <Route
          path="staff"
          element={
            <RequireAuth roles={["Owner"]}>
              <StaffPage />
            </RequireAuth>
          }
        />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  );
}
