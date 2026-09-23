vault {
  address = "http://vault:8200"
}

auto_auth {
  method "approle" {
    config = {
      role_id_file_path                   = "/vault/auth/role-id"
      secret_id_file_path                 = "/vault/auth/secret-id"
      remove_secret_id_file_after_reading = false
    }
  }
}

api_proxy {
  use_auto_auth_token = "force"
}

listener "tcp" {
  address     = "0.0.0.0:8100"
  tls_disable = true
}
