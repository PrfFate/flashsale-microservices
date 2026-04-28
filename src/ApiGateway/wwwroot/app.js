let accessToken = "";

const $ = (id) => document.getElementById(id);

function write(target, value, isError = false) {
  target.classList.toggle("error", isError);
  target.textContent = typeof value === "string" ? value : JSON.stringify(value, null, 2);
}

async function request(url, options = {}) {
  const headers = {
    "Content-Type": "application/json",
    ...(options.headers ?? {})
  };

  if (accessToken) {
    headers.Authorization = `Bearer ${accessToken}`;
  }

  const response = await fetch(url, { ...options, headers });
  const contentType = response.headers.get("content-type") ?? "";
  const payload = contentType.includes("application/json")
    ? await response.json()
    : await response.text();

  if (!response.ok) {
    throw payload;
  }

  return payload;
}

$("healthButton").addEventListener("click", async () => {
  try {
    write($("campaignOutput"), await request("/gateway/health"));
  } catch (error) {
    write($("campaignOutput"), error, true);
  }
});

$("tokenForm").addEventListener("submit", async (event) => {
  event.preventDefault();

  try {
    const payload = await request("/gateway/auth/token", {
      method: "POST",
      body: JSON.stringify({ subject: $("subject").value })
    });

    accessToken = payload.accessToken;
    write($("tokenOutput"), payload);
  } catch (error) {
    write($("tokenOutput"), error, true);
  }
});

$("campaignButton").addEventListener("click", async () => {
  try {
    write($("campaignOutput"), await request("/api/campaigns"));
  } catch (error) {
    write($("campaignOutput"), error, true);
  }
});

$("inventoryForm").addEventListener("submit", async (event) => {
  event.preventDefault();

  try {
    const productId = $("inventoryProductId").value;
    $("orderProductId").value = productId;

    const payload = await request("/api/inventory", {
      method: "POST",
      body: JSON.stringify({
        productId,
        availableQuantity: Number($("inventoryQuantity").value)
      })
    });

    write($("inventoryOutput"), payload || "Inventory updated.");
  } catch (error) {
    write($("inventoryOutput"), error, true);
  }
});

$("orderForm").addEventListener("submit", async (event) => {
  event.preventDefault();

  try {
    const payload = await request("/api/orders", {
      method: "POST",
      body: JSON.stringify({
        productId: $("orderProductId").value,
        quantity: Number($("orderQuantity").value),
        unitPrice: Number($("unitPrice").value)
      })
    });

    write($("orderOutput"), payload);
  } catch (error) {
    write($("orderOutput"), error, true);
  }
});
