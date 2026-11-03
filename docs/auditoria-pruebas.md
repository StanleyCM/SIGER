# Inventario de pruebas de SIGER

Una fila por método original. Las filas de datos de una teoría heredan su dictamen salvo las variantes retiradas (C). A: crítica; B: útil; C: redundante para este alcance; D: trivial; E: duplicada en otro flujo; F: obsoleta (no se identificó ninguna con certeza). B retirada indica cobertura secundaria sacrificada explícitamente.

## SIGER.Tests/API/AuditSecurityTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Audit_queries_require_administrator | B | Retirar |
| No_audit_write_endpoint_exists | B | Retirar |
| Actor_uses_local_claim_not_client_input_and_does_not_trust_forwarded_headers | A | Conservar |
| Unauthenticated_claim_and_missing_request_do_not_invent_an_actor | B | Retirar |
| Authenticated_request_without_local_identity_fails_instead_of_anonymous_audit | B | Retirar |

## SIGER.Tests/API/ConfigurationAndLoggingTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Startup_rejects_missing_or_unsafe_configuration | B | Retirar |
| Jwt_validation_uses_asymmetric_JWKS_with_rotation_manager | B | Retirar |
| Jwks_retriever_accepts_multiple_rotation_keys_without_network | B | Retirar |
| Request_logging_excludes_headers_query_and_body | B | Retirar |
| Provider_exception_is_logged_only_in_development_and_never_exposed_in_response | B | Retirar |

## SIGER.Tests/API/EndpointTests.cs

| Método | Clase | Decisión |
|---|---|---|
| User_compensation_failure_returns_safe_operation_reference | B | Retirar |
| Overlapping_reservation_is_a_safe_conflict | B | Retirar |
| Protected_routes_require_authentication | A | Reducir matriz: "/api/v1/users", "/api/v1/orders" |
| Roles_enforce_controller_policies | A | Reducir matriz: "Administrador", "/api/v1/users", 200, "Mesero", "/api/v1/users", 403 |
| Invalid_identity_is_rejected | B | Retirar |
| Es256_is_accepted_and_token_roles_are_not_trusted | A | Conservar |
| User_creation_returns_created_location_and_safe_dto | B | Retirar |
| Order_and_payment_use_trusted_local_user_and_item_route_id | B | Retirar |
| Waiter_cannot_bypass_payment_or_kitchen_workflows | A | Conservar |
| Client_reservations_cannot_access_or_assign_other_users | A | Conservar |
| Result_failures_are_not_200 | B | Retirar |
| Model_binding_rejects_bad_filters | B | Retirar |
| Invalid_json_and_integer_enums_are_rejected | B | Retirar |
| Login_success_failure_and_rate_limit | A | Conservar |
| Public_catalog_is_anonymous_and_rate_limited | A | Conservar |
| Cors_preflight_only_allows_configured_origin | B | Retirar |
| Development_openapi_describes_security_and_swagger_is_available | B | Retirar |
| Exception_mapping_never_discloses_internal_details | B | Retirar |

## SIGER.Tests/API/OrderWorkflowTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Real_services_complete_order_kitchen_payment_flow_through_authenticated_HTTP | A | Conservar |

## SIGER.Tests/API/PublicReservationSecurityTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Availability_is_public_read_only_uncached_and_returns_only_times_and_booleans | A | Conservar |
| Availability_rejects_invalid_queries | A | Reducir matriz: "numberOfPeople=2", "date=invalid&numberOfPeople=2" |
| Patch_is_partial_requires_own_token_and_returns_no_secrets | A | Conservar |
| Patch_rejects_missing_wrong_other_and_expired_credentials | A | Conservar |
| Patch_nonpending_returns_conflict | A | Representar el conflicto con reserva cancelada |
| Patch_rejects_internal_fields | E | Retirar |
| Guest_flow_works_without_JWT_and_token_is_only_in_creation_response | A | Conservar |
| Numeric_ID_or_another_guests_token_does_not_grant_access | A | Representar lectura sin token y escritura con token ajeno |
| Query_string_token_is_not_accepted | A | Conservar |
| Public_catalog_does_not_require_JWT | E | Retirar |
| Internal_routes_still_require_authorization | E | Retirar |
| Public_writes_are_rate_limited | A | Conservar |
| Preorder_ignores_client_total_and_operational_fields | A | Conservar |
| Invalid_and_expired_credentials_return_401 | E | Retirar |
| Vite_origin_can_send_reservation_header_but_unknown_origin_is_not_allowed | A | Conservar |

## SIGER.Tests/API/RoleAuthorizationTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Policies_use_exact_persisted_role_names | E | Retirar |
| Enrichment_preserves_one_local_role_and_local_user_identity | E | Retirar |

## SIGER.Tests/API/RouteContractTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Administrator_routes_bind_and_return_expected_success_codes | C | Retirar |
| Missing_reservation_is_not_updated | A | Conservar |
| Production_does_not_expose_swagger_or_openapi | C | Retirar |

## SIGER.Tests/Application/ClosureRegressionTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Pagination_rejects_overflow_before_repository_access | C | Retirar |
| Order_reads_start_inside_the_transaction | B | Retirar |
| Payment_reads_start_inside_the_transaction | B | Retirar |
| Repeated_order_products_are_not_queried_per_detail | B | Retirar |
| Promotion_fetches_products_in_one_batch | B | Retirar |
| Cancellation_releases_table_only_without_other_active_orders | A | Conservar |
| Quantity_change_preserves_original_price | A | Conservar |
| Kitchen_rejects_skips_repeats_and_backward_transitions | A | Reducir matriz: OrderStatus.Pending, true, OrderStatus.Ready, true, OrderStatus.Served, true |
| Waiter_cannot_serve_before_ready | A | Conservar |
| Reservation_create_and_update_reject_invalid_state | B | Retirar |
| Reactivating_reservation_rechecks_availability | B | Retirar |
| Terminal_reservations_do_not_need_a_free_slot | B | Retirar |

## SIGER.Tests/Application/GuestReservationEditTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Contact_and_notes_patch_preserves_omitted_fields_identity_and_hash | A | Conservar |
| Schedule_changes_recheck_capacity_status_two_hour_overlap_excluding_self | A | Conservar |
| Reassignment_updates_only_preorder_table_and_keeps_nonoperational_state | A | Conservar |
| Unavailable_edit_does_not_mutate_any_reservation_fields | A | Conservar |
| Only_pending_can_be_edited | A | Conservar |
| Neither_original_nor_requested_date_may_be_in_the_past | A | Conservar |
| Own_valid_credential_is_required_before_and_after_lock | A | Conservar |
| Invalid_patch_never_saves | E | Retirar |

## SIGER.Tests/Application/GuestReservationTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Valid_guest_is_pending_free_and_receives_credential_only_at_creation | A | Conservar |
| Invalid_guest_input_never_saves | A | Reducir matriz: "phone", "past", "many" |
| No_eligible_table_returns_conflict_without_changes | A | Conservar |
| Assignment_locks_then_rechecks_and_skips_overlap_in_same_transaction | A | Conservar |
| Reservation_access_requires_own_unexpired_credential | E | Retirar |
| Own_reservation_query_returns_editable_contact_but_excludes_credentials | E | Retirar |
| Internal_cancellation_cancels_preorder_without_releasing_table | A | Conservar |
| Internal_guest_reschedule_updates_preorder_table_and_credential_expiry | E | Retirar |

## SIGER.Tests/Application/OrderPaymentTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Create_uses_catalog_prices_and_atomic_table_occupation | A | Conservar |
| Create_rejects_invalid_inputs_without_writes | B | Retirar |
| Takeaway_supports_optional_table_and_client | B | Retirar |
| Item_changes_recalculate_totals | A | Conservar |
| Terminal_orders_cannot_be_edited_or_reopened | B | Retirar |
| Paid_status_requires_payment_workflow | A | Conservar |
| Missing_order_is_failure | B | Retirar |
| Item_validation_has_no_side_effects | B | Retirar |
| Account_and_status_update_preserve_mapping | B | Retirar |
| Stale_order_version_is_conflict_before_mutation | A | Conservar |
| Kitchen_happy_path_and_list | B | Retirar |
| Kitchen_rejects_terminal_states | B | Retirar |
| Payment_coordinates_payment_order_and_table_inside_transaction | A | Conservar |
| Payment_rejects_invalid_state_without_saving | A | Reducir matriz: "cancelled", "amount", "duplicate" |
| Payment_failure_is_propagated_not_reported_as_success | B | Retirar |
| Takeaway_payment_and_payment_lookup | B | Retirar |

## SIGER.Tests/Application/PreOrderTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Preorder_uses_server_prices_links_reservation_and_has_no_operational_side_effects | A | Conservar |
| Duplicate_is_conflict_and_does_not_replace_history | A | Conservar |
| Invalid_items_are_rejected_without_writes | A | Reducir matriz: "missing", "unavailable", "inactive" |
| Terminal_reservation_rejects_preorder | A | Conservar |
| Past_reservation_is_rejected_even_with_valid_credential | A | Conservar |
| Credential_is_revalidated_after_lock_and_before_writes | A | Conservar |
| Query_requires_credential_for_same_reservation | B | Retirar |
| Preorder_rejects_payment_account_kitchen_and_generic_order_mutations | A | Conservar |

## SIGER.Tests/Application/PublicCatalogTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Only_active_categories_are_exposed | A | Conservar |
| Product_lookup_requires_available_product_and_active_category | A | Conservar |
| Promotions_are_current_informational_and_only_reference_public_products | B | Retirar |

## SIGER.Tests/Application/QueryContractTests.cs

| Método | Clase | Decisión |
|---|---|---|
| User_queries_map_pagination_and_reject_invalid_pages | C | Retirar |
| Category_queries_map_pagination_and_reject_invalid_pages | C | Retirar |
| Product_queries_map_pagination_and_reject_invalid_pages | C | Retirar |
| Table_queries_map_pagination_and_reject_invalid_pages | C | Retirar |
| Order_queries_map_pagination_and_reject_invalid_pages | C | Retirar |
| Reservation_queries_map_pagination_and_reject_invalid_pages | C | Retirar |
| Promotion_queries_map_pagination_and_reject_invalid_pages | C | Retirar |
| Audit_queries_map_pagination_and_reject_invalid_pages | C | Retirar |

## SIGER.Tests/Application/ReservationAvailabilityTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Available_slots_use_Dominican_offset_and_read_only_shared_two_hour_checks | A | Conservar |
| Ineligible_tables_make_all_slots_unavailable | B | Retirar |
| People_date_and_past_slots_change_availability | A | Conservar |
| Own_reservation_is_excluded_only_with_its_valid_credential | A | Conservar |
| Invalid_query_is_rejected_before_table_reads | B | Retirar |
| Today_uses_Dominican_date_and_accepts_day_30_only | A | Conservar |

## SIGER.Tests/Application/SupportingServiceTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Reservation_create_update_cancel_and_lookup | B | Retirar |
| Reservation_rejects_invalid_input | B | Retirar |
| Missing_reservations_are_failures | B | Retirar |
| Promotion_create_deduplicates_products_and_uses_transaction | B | Retirar |
| Promotion_rejects_invalid_input | B | Retirar |
| Promotion_update_toggle_add_remove_and_duplicate_validation | B | Retirar |
| Promotion_equal_start_and_end_is_rejected_before_persistence | B | Retirar |
| Missing_promotions_and_invalid_updates_do_not_save | B | Retirar |
| Category_lifecycle_and_validation | B | Retirar |
| Reports_delegate_filters_limit_and_cancellation | B | Retirar |
| Audit_mapping_preserves_optional_actor_and_payload | B | Retirar |
| Pagination_rejects_invalid_metadata | C | Retirar |
| Result_and_pagination_contracts | C | Retirar |

## SIGER.Tests/Application/UserCatalogTests.cs

| Método | Clase | Decisión |
|---|---|---|
| User_create_normalizes_and_never_exposes_password | B | Retirar |
| Invalid_user_is_not_created | B | Retirar |
| Remote_creation_failure_prevents_local_write | B | Retirar |
| Update_and_status_delegate_and_map | B | Retirar |
| Invalid_user_update_does_not_save | B | Retirar |
| Auth_validates_local_identity_and_preserves_provider_token | B | Retirar |
| Product_create_update_and_availability | B | Retirar |
| Product_rejects_bad_name_price_or_category | B | Retirar |
| Stale_product_version_cannot_overwrite_current_record | B | Retirar |
| Table_create_update_and_status | B | Retirar |
| Table_rejects_invalid_number_or_capacity | B | Retirar |
| Missing_catalog_records_return_failure | B | Retirar |
| Stale_table_version_is_rejected_before_mutation | B | Retirar |

## SIGER.Tests/Application/UserCompensationTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Creation_succeeds_in_both_systems | B | Retirar |
| Creation_compensates_DB_failure_even_when_request_cancelled | A | Conservar |
| Creation_timeout_after_remote_commit_uses_reserved_UID_for_cleanup | B | Retirar |
| Precommit_database_outage_does_not_prevent_automatic_Auth_cleanup | B | Retirar |
| Email_update_synchronizes_without_changing_auth_ID | B | Retirar |
| Email_update_restores_Auth_if_database_fails | A | Conservar |
| Auth_email_failure_does_not_change_database | B | Retirar |
| Activation_and_deactivation_are_compensated_when_needed | B | Retirar |
| Failed_compensation_is_critical_and_safe | B | Retirar |
| Compensation_never_overwrites_a_subsequent_Auth_change | A | Conservar |
| Lost_commit_acknowledgement_does_not_delete_a_persisted_identity | A | Conservar |
| Invalid_Auth_profile_blocks_update_before_writes | B | Retirar |
| Uncertain_Auth_update_does_not_blindly_overwrite_remote_state | B | Retirar |

## SIGER.Tests/Domain/ModelContractTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Collections_are_initialized_and_not_shared | D | Retirar |
| Enum_contract_is_stable | D | Retirar |
| Composite_join_has_no_surrogate_identity | D | Retirar |
| Nullable_relationships_and_audit_payloads_match_contract | D | Retirar |
| Identity_security_and_concurrency_contracts_are_preserved | D | Retirar |
| Exceptions_preserve_message_and_cause | D | Retirar |

## SIGER.Tests/Infrastructure/AuthAndUnitOfWorkTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Login_maps_provider_session_and_sends_required_contract | B | Retirar |
| Rejected_login_returns_no_session | B | Retirar |
| Malformed_login_is_rejected_with_sanitized_error | B | Retirar |
| Create_user_maps_id_and_admin_contract | B | Retirar |
| Invalid_create_response_cannot_create_empty_local_identity | B | Retirar |
| Enable_disable_use_admin_update | B | Retirar |
| Remote_errors_do_not_expose_remote_body | B | Retirar |
| Cancellation_is_not_converted_to_authentication_success | B | Retirar |
| Save_delegates_cancellation_and_result | B | Retirar |
| Transaction_commits_and_disposes_after_success | A | Conservar |
| Transaction_rolls_back_and_preserves_operation_error | A | Conservar |
| Nested_work_does_not_commit_owners_transaction | A | Conservar |
| Null_operations_are_rejected_before_begin | B | Retirar |

## SIGER.Tests/Infrastructure/AuthCompensationTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Email_update_is_administrative_without_forcing_confirmation | B | Retirar |
| Restore_checks_snapshot_then_restores_only_changed_fields | B | Retirar |
| Later_remote_change_prevents_restore_or_delete | B | Retirar |
| Creation_cleanup_deletes_only_the_verified_identity | B | Retirar |
| Missing_Auth_identity_is_not_successfully_updated | B | Retirar |
| Failed_compensation_emits_structured_critical_without_exception_payloads | B | Retirar |

## SIGER.Tests/Infrastructure/BusinessAuditTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Create_commits_audit_with_generated_ID_and_actor | A | Conservar |
| Update_captures_original_and_new_price_without_navigation_cycles | B | Retirar |
| Status_mutations_have_consistent_actions | B | Retirar |
| Kitchen_waiter_and_cancellation_keep_transition_values | B | Retirar |
| Payment_completion_and_detail_change_are_audited | B | Retirar |
| Reservation_transitions_are_audited | B | Retirar |
| Composite_association_and_physical_delete_preserve_keys | B | Retirar |
| Profile_free_text_and_secrets_are_never_serialized | A | Conservar |
| Audit_failure_rolls_back_business_even_if_outer_transaction_catches | A | Conservar |
| Business_failure_leaves_no_success_audit | B | Retirar |
| Outer_rollback_removes_business_and_audit | A | Conservar |
| Queries_and_noop_saves_do_not_generate_audit_and_false_preserves_tracking | B | Retirar |
| Many_changes_are_batched_not_one_audit_command_per_entity | B | Retirar |
| Auth_creation_and_audit_commit_together_or_compensate | B | Retirar |
| Table_administrative_status_has_previous_and_current_state | B | Retirar |
| Missing_or_invalid_address_is_nullable | B | Retirar |
| Invalid_actor_is_rejected_before_any_write | B | Retirar |
| Synchronous_audit_failure_rolls_back_business | B | Retirar |

## SIGER.Tests/Infrastructure/ClosureSqlTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Product_batch_is_one_filtered_query_and_empty_batch_has_no_IO | B | Retirar |
| Product_pagination_joins_category_filters_and_has_stable_SQL_order | C | Retirar |
| Reservation_overlap_is_server_side_exists_with_half_open_two_hour_interval | A | Conservar |
| Table_and_reservation_locks_refuse_to_run_without_transaction | A | Conservar |

## SIGER.Tests/Infrastructure/ExistingSchemaContractTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Mandatory_historical_relationships_restrict_parent_deletion | C | Retirar |
| Mandatory_domain_fields_match_NOT_NULL_relational_contract | C | Retirar |
| Mapped_column_matches_existing_database | C | Retirar |
| Payment_order_is_unique_but_user_email_is_not_a_database_unique_constraint | A | Conservar |

## SIGER.Tests/Infrastructure/GuestPreOrderModelTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Guest_relationships_and_unique_indexes_match_approved_schema | A | Conservar |

## SIGER.Tests/Infrastructure/GuestPreOrderPostgresTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Script_is_atomic_repeatable_and_preserves_legacy_data_indexes_and_RLS | A | Conservar |
| Script_reuses_equivalent_indexes_under_other_names | B | Retirar |
| Incompatible_existing_column_aborts_and_rolls_back_nullability_changes | A | Conservar rollback de migración |
| Concurrent_guests_competing_for_one_table_create_only_one_reservation | A | Conservar |
| Half_open_intervals_allow_back_to_back_reservations | E | Retirar |
| Concurrent_preorders_are_unique_persist_after_reload_and_do_not_enter_kitchen | A | Conservar |
| Cancellation_is_transactional_and_preserves_occupied_table_and_history | A | Conservar |
| Database_enforces_uniqueness_ownership_contact_and_credential_pairs | A | Conservar |
| Public_catalog_filters_in_SQL_before_pagination_and_audit_excludes_contacts_and_credentials | C | Retirar |
| Guest_edit_reassigns_preorder_preserving_credentials_prices_and_nonoperational_state | E | Retirar |
| Guest_edit_excludes_self_allows_adjacent_intervals_and_failure_preserves_every_field | E | Retirar |
| Failed_edit_save_rolls_back_both_reservation_and_preorder | A | Conservar |
| Concurrent_edits_competing_for_one_slot_have_one_winner | A | Conservar |
| Availability_uses_real_Postgres_overlap_status_capacity_and_contiguous_intervals | A | Conservar |

## SIGER.Tests/Infrastructure/GuestReservationTokenTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Credentials_have_256_random_bits_and_fixed_size_hashes | B | Retirar |
| Malformed_credentials_fail | B | Retirar |
| Only_matching_hash_before_expiry_is_valid | A | Conservar |

## SIGER.Tests/Infrastructure/ModelTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Available_products_join_uses_existing_category_estado_column | B | Retirar |
| Repeated_scopes_reuse_the_same_EF_model | B | Retirar |
| Tables_identity_keys_and_physical_columns | B | Retirar |
| Composite_key_and_delete_rules_match_relational_model | B | Retirar |
| Monetary_precision_is_explicit | A | Consolidar precios, subtotales, cobros y total en un contrato monetario |
| PostgreSql_specific_types_lengths_and_indexes | B | Retirar |
| Native_enums_use_documented_spanish_labels | B | Retirar |
| Version_initializes_and_increments_without_discarding_original | A | Conservar |
| Updating_tracked_order_keeps_catalog_and_user_unchanged_and_tracks_item_changes | A | Conservar |

## SIGER.Tests/Infrastructure/ReportSqlTests.cs

| Método | Clase | Decisión |
|---|---|---|
| Real_report_repository_translates_aggregates_to_SQL_without_database_IO | B | Retirar |

## ProductCatalog.test.jsx

| Caso | Clase | Decisión |
|---|---|---|
| agrupa por categoryId y ordena las conocidas primero, luego las adicionales por nombre | A | Conservar |
| filtra una categoría por API y restaura todas agrupadas | A | Conservar |
| agrega, aumenta, disminuye y elimina desde la card manteniendo el resumen sincronizado | A | Conservar |
| refleja cambios del resumen en la card y conserva la cantidad al cambiar el filtro | A | Conservar |
| respeta el máximo de 100 unidades y bloquea controles durante el envío | C | Retirar variante del mismo flujo |

## ReservationAvailability.test.jsx

| Caso | Clase | Decisión |
|---|---|---|
| habilita solo los libres y deshabilita llenos y pasados sin permitir seleccionarlos | A | Conservar |
| refresca por personas y fecha y limpia la selección cuando deja de estar libre | A | Conservar |
| conserva una selección si continúa disponible tras cambiar personas | C | Retirar variante del mismo flujo |
| bloquea durante la carga, muestra fallo discreto y permite reintentar sin asumir disponibilidad | A | Conservar |
| ignora una respuesta antigua después de cambiar personas | A | Conservar |
| envía la credencial solo al consultar disponibilidad de su propia reserva en edición | A | Conservar |

## ReservationContact.test.jsx

| Caso | Clase | Decisión |
|---|---|---|
| elimina letras y limpia texto pegado conservando ceros iniciales | C | Retirar variante del mismo flujo |
| exige 10 dígitos, limita a 15 y bloquea el envío de un teléfono incompleto | A | Conservar |
| permite correo vacío y actualiza el error en vivo para dominios generales | A | Conservar |
| impide enviar un correo inválido aunque el resto del formulario esté completo | A | Conservar |
| inicia en 2, aumenta con teclado y disminuye sin salir del rango 1–12 | C | Retirar variante del mismo flujo |
| envía la cantidad del stepper como numberOfPeople y mantiene el offset correcto | C | Retirar variante del mismo flujo |

## ReservationFlow.test.jsx

| Caso | Clase | Decisión |
|---|---|---|
| muestra el formulario accesible y el catálogo de la API | C | Retirar variante del mismo flujo |
| crea la reserva pendiente, admite correo omitido y oculta la credencial | A | Conservar |
| permite pasar de la reserva a una preorden opcional | C | Retirar variante del mismo flujo |
| edita la selección y crea la preorden sin precios, mostrando el total del servidor | A | Conservar |
| RESERVA reinicia desde Home después de una preorden y desde Menu sin alterar lo guardado | C | Retirar variante del mismo flujo |
| descarta el borrador de preorden y la credencial anterior al crear otra reserva | A | Conservar |
| no permite reiniciar mientras se está guardando una preorden | C | Retirar variante del mismo flujo |
| precarga el formulario de edición y cancelar no toca el servidor | A | Conservar |
| guarda edición con el mismo token y permite editar después de crear preorden | A | Conservar |
| mantiene lo escrito ante falta de disponibilidad y no altera la reserva guardada | A | Conservar |
| el botón global RESERVA también reinicia al pulsarlo dentro del formulario de edición | C | Retirar variante del mismo flujo |

## ReservationSchedule.test.jsx

| Caso | Clase | Decisión |
|---|---|---|
| requiere fecha y horario con mensajes dentro del formulario | C | Retirar variante del mismo flujo |
| permite un único slot y deshabilita horarios pasados en Santo Domingo | A | Conservar |
| usa el día dominicano incluso cuando UTC ya cambió de día y año | A | Conservar |
| acepta el día 30 y rechaza horas fuera de la configuración | A | Conservar |
| permite ambos límites y devuelve el foco al selector al elegir o cerrar | C | Retirar variante del mismo flujo |
| muestra los límites del calendario con el día dominicano al cambiar el año UTC | C | Retirar variante del mismo flujo |
| Rechaza fechas fuera de rango (2 límites) | A | Conservar |
